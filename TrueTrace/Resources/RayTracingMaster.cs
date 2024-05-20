using System.Collections.Generic;
using UnityEngine;
using CommonVars;
using UnityEngine.SceneManagement;
using UnityEngine.Rendering;
using System.IO;
using System.Xml;
using System.Xml.Serialization;

namespace TrueTrace {
    public class RayTracingMaster : MonoBehaviour
    {
        [HideInInspector] public static Camera _camera;
        
        [HideInInspector] public AtmosphereGenerator Atmo;
        [HideInInspector] public AssetManager Assets;
        private ReSTIRASVGF ReSTIRASVGFCode;
        private ReCurDenoiser ReCurDen;
        private Denoiser Denoisers;
        private ASVGF ASVGFCode;
        private bool Abandon = false;
        #if UseOIDN
            private UnityDenoiserPlugin.DenoiserPluginWrapper OIDNDenoiser;
        #endif
        [HideInInspector] public static bool DoSaving = true;
        public static RayObjs raywrites = new RayObjs();
        public static void WriteString(RayTracingObject OBJtoWrite, string NameIndex) {
            if(DoSaving) {
                if(OBJtoWrite == null || OBJtoWrite.gameObject == null) return;
                int ID = OBJtoWrite.gameObject.GetInstanceID();
                int Index = (new List<string>(OBJtoWrite.Names)).IndexOf(NameIndex);
                RayObjectDatas TempOBJ = raywrites.RayObj.Find((s1) => (s1.MatName.Equals(NameIndex) && s1.ID.Equals(ID)));
                int WriteID = raywrites.RayObj.IndexOf(TempOBJ);
                RayObjectDatas DataToWrite = new RayObjectDatas() {
                    ID = ID,
                    MatName = NameIndex,
                    OptionID = (int)OBJtoWrite.MaterialOptions[Index],
                    TransCol = OBJtoWrite.TransmissionColor[Index],
                    BaseCol = OBJtoWrite.BaseColor[Index],
                    MetRemap = OBJtoWrite.MetallicRemap[Index],
                    RoughRemap = OBJtoWrite.RoughnessRemap[Index],
                    Emiss = OBJtoWrite.emmission[Index],
                    EmissCol = OBJtoWrite.EmissionColor[Index],
                    Rough = OBJtoWrite.Roughness[Index],
                    IOR = OBJtoWrite.IOR[Index],
                    Met = OBJtoWrite.Metallic[Index],
                    SpecTint = OBJtoWrite.SpecularTint[Index],
                    Sheen = OBJtoWrite.Sheen[Index],
                    SheenTint = OBJtoWrite.SheenTint[Index],
                    Clearcoat = OBJtoWrite.ClearCoat[Index],
                    ClearcoatGloss = OBJtoWrite.ClearCoatGloss[Index],
                    Anisotropic = OBJtoWrite.Anisotropic[Index],
                    Flatness = OBJtoWrite.Flatness[Index],
                    DiffTrans = OBJtoWrite.DiffTrans[Index],
                    SpecTrans = OBJtoWrite.SpecTrans[Index],
                    FollowMat = OBJtoWrite.FollowMaterial[Index],
                    ScatterDist = OBJtoWrite.ScatterDist[Index],
                    Spec = OBJtoWrite.Specular[Index],
                    AlphaCutoff = OBJtoWrite.AlphaCutoff[Index],
                    NormStrength = OBJtoWrite.NormalStrength[Index],
                    Hue = OBJtoWrite.Hue[Index],
                    Brightness = OBJtoWrite.Brightness[Index],
                    Contrast = OBJtoWrite.Contrast[Index],
                    Saturation = OBJtoWrite.Saturation[Index],
                    BlendColor = OBJtoWrite.BlendColor[Index],
                    BlendFactor = OBJtoWrite.BlendFactor[Index],
                    MainTexScaleOffset = OBJtoWrite.MainTexScaleOffset[Index],
                    SecondaryTextureScale = OBJtoWrite.SecondaryTextureScale[Index],
                    Rotation = OBJtoWrite.Rotation[Index],
                    Flags = OBJtoWrite.Flags[Index]

                };
                if(WriteID == -1) {
                    raywrites.RayObj.Add(DataToWrite);
                } else {
                    raywrites.RayObj[WriteID] = DataToWrite;
                }
            }
        }

        [HideInInspector] public ComputeShader ShadingShader;
        private ComputeShader IntersectionShader;
        private ComputeShader GenerateShader;
        private ComputeShader ReSTIRGI;
        private ComputeShader CDFCompute;

        private RenderTexture _target;
        private RenderTexture _converged;
        private RenderTexture _DebugTex;
        private RenderTexture _FinalTex;
        private RenderTexture CorrectedDistanceTex;
        private RenderTexture CorrectedDistanceTexB;
        private RenderTexture _RandomNums;
        private RenderTexture _RandomNumsB;
        private RenderTexture _PrimaryTriangleInfo;

        private RenderTexture GIReservoirA;
        private RenderTexture GIReservoirB;
        private RenderTexture GIReservoirC;

        private RenderTexture GIWorldPosA;
        private RenderTexture GIWorldPosB;
        private RenderTexture GIWorldPosC;

        private RenderTexture GINEEPosA;
        private RenderTexture GINEEPosB;
        private RenderTexture GINEEPosC;

        private RenderTexture CDFX;
        private RenderTexture CDFY;

        private RenderTexture Gradients;


        #if TTLightMapping
            public RenderTexture LightMapTemp; 
            public int LightMappingSampleCount = 150;
            private RenderTexture LightWorldIndex;
            public LightmapData[] lightmaps;
            public int CurrentLightmapIndex;
            public ComputeBuffer LightMapTrisBuffer;
        #endif


        [HideInInspector] public RenderTexture ScreenSpaceInfo;
        private RenderTexture ScreenSpaceInfoPrev;

        private ComputeBuffer _RayBuffer;
        private ComputeBuffer LightingBuffer;
        private ComputeBuffer PrevLightingBufferA;
        private ComputeBuffer PrevLightingBufferB;
        private ComputeBuffer _BufferSizes;
        private ComputeBuffer _ShadowBuffer;
        private ComputeBuffer RaysBuffer;
        private ComputeBuffer RaysBufferB;
        private ComputeBuffer CurBounceInfoBuffer;
        private ComputeBuffer CDFTotalBuffer;

        #if UseOIDN
            private GraphicsBuffer ColorBuffer;
            private GraphicsBuffer OutputBuffer;
            private GraphicsBuffer AlbedoBuffer;
            private GraphicsBuffer NormalBuffer;
        #endif
        #if !UseRadianceCache
            private ComputeBuffer CacheBuffer;
            private ComputeBuffer VoxelDataBufferA;
            private ComputeBuffer VoxelDataBufferB;
            private ComputeBuffer HashBuffer;
            private ComputeBuffer HashBufferB;
        #endif

        private Texture3D ToneMapTex;
        private Texture3D ToneMapTex2;
        private Material _addMaterial;
        private Material _FireFlyMaterial;
        [HideInInspector] public int _currentSample = 0;
        private static bool _meshObjectsNeedRebuilding = false;
        public static List<RayTracingLights> _rayTracingLights = new List<RayTracingLights>();

        private float _lastFieldOfView;

        [HideInInspector] public int FramesSinceStart2;
        private BufferSizeData[] BufferSizes;
        [SerializeField] [HideInInspector] public int SampleCount;

        private int uFirstFrame = 1;
        [HideInInspector] public static bool DoDing = true;
        [HideInInspector] public static bool DoCheck = false;
        [HideInInspector] public float IndirectBoost = 1;
        [HideInInspector] public int bouncecount = 24;
        [HideInInspector] public bool ClayMode = false;
        [HideInInspector] public bool UseRussianRoulette = true;
        [HideInInspector] public bool UseNEE = true;
        [HideInInspector] public bool DoTLASUpdates = true;
        [HideInInspector] public bool AllowConverge = true;
        [HideInInspector] public bool AllowBloom = false;
        [HideInInspector] public bool AllowDoF = false;
        [HideInInspector] public bool AllowAutoExpose = false;
        [HideInInspector] public bool AllowToneMap = true;
        [HideInInspector] public bool AllowTAA = false;
        [HideInInspector] public float DoFAperature = 0.2f;
        [HideInInspector] public float DoFAperatureScale = 1.0f;
        [HideInInspector] public float DoFFocal = 0.2f;
        [HideInInspector] public float RenderScale = 1.0f;
        [HideInInspector] public float BloomStrength = 32.0f;
        [HideInInspector] public float MinSpatialSize = 10.0f;
        [HideInInspector] public bool UseASVGF = false;
        [HideInInspector] public bool UseReCur = false;
        [HideInInspector] public bool UseTAAU = true;
        [HideInInspector] public bool DoExposureAuto = false;
        [HideInInspector] public int ReSTIRGIUpdateRate = 0;
        [HideInInspector] public bool UseReSTIRGITemporal = false;
        [HideInInspector] public bool UseReSTIRGISpatial = false;
        [HideInInspector] public bool UseReSTIRGI = false;
        [HideInInspector] public int ReSTIRGISpatialCount = 5;
        [HideInInspector] public int ReSTIRGITemporalMCap = 0;
        [HideInInspector] public bool DoReSTIRGIConnectionValidation = false;
        [HideInInspector] public float Exposure = 1;
        [HideInInspector] public float ReCurBlurRadius = 30.0f;
        [HideInInspector] public bool PrevReSTIRGI = false;
        [HideInInspector] public bool DoPartialRendering = false;
        [HideInInspector] public int PartialRenderingFactor = 1;
        [HideInInspector] public bool DoFirefly = false;
        [HideInInspector] public bool ImprovedPrimaryHit = false;
        [HideInInspector] public int RISCount = 5;
        [HideInInspector] public int ToneMapper = 0;
        [HideInInspector] public float SunDesaturate = 0;
        [HideInInspector] public float SkyDesaturate = 0;
        [HideInInspector] public Vector3 ClayColor = new Vector3(0.5f, 0.5f, 0.5f);
        [HideInInspector] public Vector3 GroundColor = new Vector3(0.1f, 0.1f, 0.1f);
        [HideInInspector] public int FireflyFrameCount = 0;
        [HideInInspector] public float FireflyStrength = 1.0f;
        [HideInInspector] public float FireflyOffset = 0.0f;
        [HideInInspector] public int OIDNFrameCount = 0;
        [HideInInspector] public bool UseOIDN = false;
        [HideInInspector] public bool DoSharpen = false;
        [HideInInspector] public float Sharpness = 1.0f;

        public static bool SceneIsRunning = false;

        [HideInInspector] public int BackgroundType = 0;
        [HideInInspector] public Vector3 SceneBackgroundColor = Vector3.one;
        [HideInInspector] public Texture SkyboxTexture;
        [HideInInspector] public float BackgroundIntensity = 1;
        private bool MeshOrderChanged = false;


        [HideInInspector] public int AtmoNumLayers = 4;
        private float PrevResFactor;
        private int GenKernel;
        private int GenASVGFKernel;
        private int GenLightmapKernel;
        private int TraceKernel;
        private int ShadowKernel;
        private int HeightmapKernel;
        private int HeightmapShadowKernel;
        private int ShadeKernel;
        private int FinalizeKernel;
        private int GIReTraceKernel;
        private int TransferKernel;
        private int CorrectedDistanceKernel;
        private int ReSTIRGIKernel;
        private int ReSTIRGISpatialKernel;
        private int TTtoOIDNKernel;
        private int OIDNtoTTKernel;
        #if !UseRadianceCache
            private int ResolveKernel;
        #endif
        private int TargetWidth;
        private int TargetHeight;
        [HideInInspector] public int FramesSinceStart;
        [System.NonSerialized] public int SourceWidth;
        [System.NonSerialized] public int SourceHeight;
        private Vector3 PrevCamPosition;
        private bool PrevASVGF;
        private bool PrevReCur;
        private Matrix4x4 PrevViewProjection;


        [System.Serializable]
        public struct BufferSizeData
        {
            public int tracerays;
            public int shadow_rays;
            public int heightmap_rays;
            public int Heightmap_shadow_rays;
        }


        public void TossCamera(Camera camera) {
            _camera = camera;
        }
        unsafe public void Start2()
        {
            #if TTLightMapping
                CurrentLightmapIndex = 0;
                lightmaps = LightmapSettings.lightmaps;
            #endif
            Application.targetFrameRate = 165;
            ASVGFCode = new ASVGF();
            ReCurDen = new ReCurDenoiser();
            ReSTIRASVGFCode = new ReSTIRASVGF();
            ToneMapTex = Resources.Load<Texture3D>("Utility/ToneMapTex");
            ToneMapTex2 = Resources.Load<Texture3D>("Utility/AgXBC");
            if (ShadingShader == null) {ShadingShader = Resources.Load<ComputeShader>("MainCompute/RayTracingShader"); }
            if (IntersectionShader == null) {IntersectionShader = Resources.Load<ComputeShader>("MainCompute/IntersectionKernels"); }
            if (GenerateShader == null) {GenerateShader = Resources.Load<ComputeShader>("MainCompute/RayGenKernels"); }
            if (ReSTIRGI == null) {ReSTIRGI = Resources.Load<ComputeShader>("MainCompute/ReSTIRGI"); }
            TargetWidth = 1;
            TargetHeight = 1;
            SourceWidth = 1;
            SourceHeight = 1;
            PrevResFactor = RenderScale;
            _meshObjectsNeedRebuilding = true;
            Assets = gameObject.GetComponent<AssetManager>();
            Assets.BuildCombined();
            uFirstFrame = 1;
            FramesSinceStart = 0;
            GenKernel = GenerateShader.FindKernel("Generate");
            GenASVGFKernel = GenerateShader.FindKernel("GenerateASVGF");
            TraceKernel = IntersectionShader.FindKernel("kernel_trace");
            ShadowKernel = IntersectionShader.FindKernel("kernel_shadow");
            ShadeKernel = ShadingShader.FindKernel("kernel_shade");
            FinalizeKernel = ShadingShader.FindKernel("kernel_finalize");
            HeightmapShadowKernel = IntersectionShader.FindKernel("kernel_shadow_heightmap");
            HeightmapKernel = IntersectionShader.FindKernel("kernel_heightmap");
            GIReTraceKernel = GenerateShader.FindKernel("GIReTraceKernel");
            TransferKernel = ShadingShader.FindKernel("TransferKernel");
            CorrectedDistanceKernel = ShadingShader.FindKernel("DepthCopyKernel");
            ReSTIRGIKernel = ReSTIRGI.FindKernel("ReSTIRGIKernel");
            ReSTIRGISpatialKernel = ReSTIRGI.FindKernel("ReSTIRGISpatial");
            GenLightmapKernel = GenerateShader.FindKernel("LightMapGen");
            TTtoOIDNKernel = ShadingShader.FindKernel("TTtoOIDNKernel");
            OIDNtoTTKernel = ShadingShader.FindKernel("OIDNtoTTKernel");
            #if !UseRadianceCache
                ResolveKernel = GenerateShader.FindKernel("CacheResolve");
            #endif
            ASVGFCode.Initialized = false;
            ReSTIRASVGFCode.Initialized = false;

            Atmo = new AtmosphereGenerator(6360, 6420, AtmoNumLayers);
            FramesSinceStart2 = 0;
            Denoisers = new Denoiser();
            Denoisers.Initialized = false;
        }

        private void OnEnable() {
            _currentSample = 0;
        }

        void OnDestroy() {
            #if UNITY_EDITOR
                using(StreamWriter writer = new StreamWriter(Application.dataPath + "/TrueTrace/Resources/Utility/SaveFile.xml")) {
                    var serializer = new XmlSerializer(typeof(RayObjs));
                    serializer.Serialize(writer.BaseStream, raywrites);
                    UnityEditor.AssetDatabase.Refresh();
                }
            #endif
        }
        public void OnDisable() {
            DoCheck = true;
            _RayBuffer?.Release();
            LightingBuffer?.Release();
            PrevLightingBufferA?.Release();
            PrevLightingBufferB?.Release();
            _BufferSizes?.Release();
            _ShadowBuffer?.Release();
            RaysBuffer?.Release();
            RaysBufferB?.Release();
            #if UseOIDN
                ColorBuffer?.Release();
                OutputBuffer?.Release();
                AlbedoBuffer?.Release();
                NormalBuffer?.Release();
                OIDNDenoiser.Dispose();
            #endif
            if(ASVGFCode != null) ASVGFCode.ClearAll();
            if(ReSTIRASVGFCode != null) ReSTIRASVGFCode.ClearAll();
            if(ReCurDen != null) ReCurDen.ClearAll();
            CurBounceInfoBuffer?.Release();
            Denoisers.ClearAll();
            CDFX.ReleaseSafe();
            CDFY.ReleaseSafe();
            CDFTotalBuffer.ReleaseSafe();
            #if !UseRadianceCache
                CacheBuffer.ReleaseSafe();
                VoxelDataBufferA.ReleaseSafe();
                VoxelDataBufferB.ReleaseSafe();
                HashBuffer.ReleaseSafe();
                HashBufferB.ReleaseSafe();
            #endif
        }

        private void RunUpdate() {
            ShadingShader.SetVector("SunDir", Assets.SunDirection);
            if (!AllowConverge) {
                SampleCount = 0;
                FramesSinceStart = 0;
            }

            if (_camera.fieldOfView != _lastFieldOfView) {
                FramesSinceStart = 0;
                _lastFieldOfView = _camera.fieldOfView;
            }

            if (_camera.transform.hasChanged) {
                SampleCount = 0;
                FramesSinceStart = 0;
                _camera.transform.hasChanged = false;
            }
        }

        public static void RegisterObject(RayTracingLights obj) {//Adds meshes to list
            _rayTracingLights.Add(obj);
        }
        public static void UnregisterObject(RayTracingLights obj) {//Removes meshes from list
            _rayTracingLights.Remove(obj);
        }

        public bool RebuildMeshObjectBuffers(CommandBuffer cmd)
        {
            cmd.BeginSample("Full Update");
            if (uFirstFrame != 1) {
                if (DoTLASUpdates) {
                    int UpdateFlags = Assets.UpdateTLAS(cmd);
                    if (UpdateFlags == 1 || UpdateFlags == 3) {
                        MeshOrderChanged = true;
                        uFirstFrame = 1;
                    } else if(UpdateFlags == 2) {
                        MeshOrderChanged = false;
                    } else if(UpdateFlags != -1) {
                        MeshOrderChanged = false;
                    } else return false;
                }
            }
            cmd.EndSample("Full Update");
            if (!_meshObjectsNeedRebuilding) return true;
            _meshObjectsNeedRebuilding = false;
            FramesSinceStart = 0;


            if(CurBounceInfoBuffer != null) CurBounceInfoBuffer.Release();
            CurBounceInfoBuffer = new ComputeBuffer(1, 12);
            if(_RayBuffer == null || _RayBuffer.count != SourceWidth * SourceHeight) {
                CommonFunctions.CreateDynamicBuffer(ref _RayBuffer, SourceWidth * SourceHeight * 2, 48);
                CommonFunctions.CreateDynamicBuffer(ref _ShadowBuffer, SourceWidth * SourceHeight, 48);
                CommonFunctions.CreateDynamicBuffer(ref LightingBuffer, SourceWidth * SourceHeight, 64);
                CommonFunctions.CreateDynamicBuffer(ref PrevLightingBufferA, SourceWidth * SourceHeight, 64);
                CommonFunctions.CreateDynamicBuffer(ref PrevLightingBufferB, SourceWidth * SourceHeight, 64);
                CommonFunctions.CreateDynamicBuffer(ref RaysBuffer, SourceWidth * SourceHeight, 24);
                CommonFunctions.CreateDynamicBuffer(ref RaysBufferB, SourceWidth * SourceHeight, 24);
            }
            GenerateShader.SetBuffer(GenASVGFKernel, "Rays", RaysBuffer);
            return true;
        }

       
        private void SetMatrix(string Name, Matrix4x4 Mat) {
            ShadingShader.SetMatrix(Name, Mat);
            IntersectionShader.SetMatrix(Name, Mat);
            GenerateShader.SetMatrix(Name, Mat);
            ReSTIRGI.SetMatrix(Name, Mat);
        }

        private void SetVector(string Name, Vector3 IN) {
            ShadingShader.SetVector(Name, IN);
            IntersectionShader.SetVector(Name, IN);
            GenerateShader.SetVector(Name, IN);
            ReSTIRGI.SetVector(Name, IN);
        }

        private void SetInt(string Name, int IN, CommandBuffer cmd) {
            cmd.SetComputeIntParam(ShadingShader, Name, IN);
            cmd.SetComputeIntParam(IntersectionShader, Name, IN);
            cmd.SetComputeIntParam(GenerateShader, Name, IN);
            cmd.SetComputeIntParam(ReSTIRGI, Name, IN);
        }

        private void SetFloat(string Name, float IN) {
            ShadingShader.SetFloat(Name, IN);
            IntersectionShader.SetFloat(Name, IN);
            GenerateShader.SetFloat(Name, IN);
            ReSTIRGI.SetFloat(Name, IN);
        }

        private void SetBool(string Name, bool IN) {
            ShadingShader.SetBool(Name, IN);
            IntersectionShader.SetBool(Name, IN);
            GenerateShader.SetBool(Name, IN);
            ReSTIRGI.SetBool(Name, IN);
        }

        Vector3 PrevPos;
        private Vector2 HDRIParams = Vector2.zero;
        private void SetShaderParameters(CommandBuffer cmd)
        {
            if(RenderScale != 1.0f) _camera.renderingPath = RenderingPath.DeferredShading;
            _camera.depthTextureMode |= DepthTextureMode.MotionVectors;
            if(UseReSTIRGI && UseASVGF && !ReSTIRASVGFCode.Initialized) ReSTIRASVGFCode.init(SourceWidth, SourceHeight);
            else if ((!UseASVGF || !UseReSTIRGI) && ReSTIRASVGFCode.Initialized) ReSTIRASVGFCode.ClearAll();
            if (!UseReSTIRGI && UseASVGF && !ASVGFCode.Initialized) ASVGFCode.init(SourceWidth, SourceHeight);
            else if ((!UseASVGF || UseReSTIRGI) && ASVGFCode.Initialized) ASVGFCode.ClearAll();
            if (!UseReCur && PrevReCur) ReCurDen.ClearAll();
            else if(!PrevReCur && UseReCur) ReCurDen.init(SourceWidth, SourceHeight);
            if(Denoisers.Initialized == false) Denoisers.init(SourceWidth, SourceHeight);

            BufferSizes = new BufferSizeData[bouncecount + 1];
            #if TTLightMapping
            #else
                BufferSizes[0].tracerays = SourceWidth * SourceHeight;
                BufferSizes[0].heightmap_rays = SourceWidth * SourceHeight;
            #endif
            if(_BufferSizes == null) {
                _BufferSizes = new ComputeBuffer(bouncecount + 1, 16);
            }
            if(_BufferSizes.count != bouncecount + 1) {
                _BufferSizes.Release();
                _BufferSizes = new ComputeBuffer(bouncecount + 1, 16);
            }
            _BufferSizes.SetData(BufferSizes);
            Shader.SetGlobalBuffer("BufferSizes", _BufferSizes);
            ShadingShader.SetComputeBuffer(TransferKernel, "BufferData", CurBounceInfoBuffer);
            ShadingShader.SetComputeBuffer(ShadeKernel, "BufferData", CurBounceInfoBuffer);

            SetMatrix("CamInvProj", _camera.projectionMatrix.inverse);
            SetMatrix("CamToWorld", _camera.cameraToWorldMatrix);
            SetMatrix("ViewMatrix", _camera.worldToCameraMatrix);
            var E = _camera.transform.position - PrevPos;
            SetVector("Up", _camera.transform.up);
            SetVector("Right", _camera.transform.right);
            SetVector("Forward", _camera.transform.forward);
            SetVector("CamPos", _camera.transform.position);
            Vector3 Temp22 = PrevPos;
            PrevPos = _camera.transform.position;
            SetVector("PrevCamPos", Temp22);
            SetVector("CamDelta", E);
            SetVector("BackgroundColor", SceneBackgroundColor);
            SetVector("ClayColor", ClayColor);
            SetVector("GroundColor", GroundColor);
            if(UseASVGF && !UseReSTIRGI) ASVGFCode.shader.SetVector("CamDelta", E);
            if(UseASVGF && UseReSTIRGI) ReSTIRASVGFCode.shader.SetVector("CamDelta", E);

            Shader.SetGlobalInt("PartialRenderingFactor", PartialRenderingFactor);
            SetFloat("FarPlane", _camera.farClipPlane);
            SetFloat("NearPlane", _camera.nearClipPlane);
            SetFloat("focal_distance", DoFFocal);
            SetFloat("AperatureRadius", DoFAperature * DoFAperatureScale);
            SetFloat("IndirectBoost", IndirectBoost);
            SetFloat("GISpatialRadius", MinSpatialSize);
            SetFloat("SunDesaturate", SunDesaturate);
            SetFloat("SkyDesaturate", SkyDesaturate);
            SetFloat("BackgroundIntensity", BackgroundIntensity);

            SetInt("AlbedoAtlasSize", Assets.AlbedoAtlasSize, cmd);
            SetInt("LightMeshCount", Assets.LightMeshCount, cmd);
            SetInt("unitylightcount", Assets.UnityLightCount, cmd);
            SetInt("screen_width", SourceWidth, cmd);
            SetInt("screen_height", SourceHeight, cmd);
            SetInt("MaxBounce", bouncecount - 1, cmd);
            SetInt("frames_accumulated", _currentSample % 65000, cmd);
            SetInt("ReSTIRGISpatialCount", ReSTIRGISpatialCount, cmd);
            SetInt("ReSTIRGITemporalMCap", ReSTIRGITemporalMCap, cmd);
            SetInt("curframe", FramesSinceStart2, cmd);
            SetInt("TerrainCount", Assets.Terrains.Count, cmd);
            SetInt("ReSTIRGIUpdateRate", UseReSTIRGI ? ReSTIRGIUpdateRate : 0, cmd);
            SetInt("RISCount", RISCount, cmd);
            SetInt("BackgroundType", BackgroundType, cmd);
            SetInt("MaterialCount", Assets.MatCount, cmd);
            SetInt("PartialRenderingFactor", DoPartialRendering ? PartialRenderingFactor : 1, cmd);

            SetBool("ClayMode", ClayMode);
            SetBool("UseReCur", UseReCur);
            SetBool("ImprovedPrimaryHit", ImprovedPrimaryHit);
            SetBool("UseRussianRoulette", UseRussianRoulette);
            SetBool("UseNEE", UseNEE);
            SetBool("UseDoF", AllowDoF);
            SetBool("UseReSTIRGI", UseReSTIRGI);
            SetBool("UseReSTIRGITemporal", UseReSTIRGITemporal);
            SetBool("UseReSTIRGISpatial", UseReSTIRGISpatial);
            SetBool("DoReSTIRGIConnectionValidation", DoReSTIRGIConnectionValidation);
            SetBool("UseASVGF", UseASVGF && !UseReSTIRGI);
            SetBool("TerrainExists", Assets.Terrains.Count != 0);
            SetBool("DoPartialRendering", DoPartialRendering);

            SetBool("DiffRes", RenderScale != 1.0f);
            SetBool("DoPartialRendering", DoPartialRendering);
            SetBool("DoExposure", AllowAutoExpose);
            ShadingShader.SetBuffer(ShadeKernel, "Exposure", Denoisers.ExposureBuffer);

            bool FlipFrame = (FramesSinceStart2 % 2 == 0);


            ShadingShader.SetTextureFromGlobal(CorrectedDistanceKernel, "Depth", "_CameraDepthTexture");
            GenerateShader.SetTextureFromGlobal(GIReTraceKernel, "MotionVectors", "_CameraMotionVectorsTexture");
            ReSTIRGI.SetTextureFromGlobal(ReSTIRGIKernel, "MotionVectors", "_CameraMotionVectorsTexture");
            ShadingShader.SetTextureFromGlobal(ShadeKernel, "MotionVectors", "_CameraMotionVectorsTexture");

            ShadingShader.SetTextureFromGlobal(FinalizeKernel, "DiffuseGBuffer", "_CameraGBufferTexture0");
            ShadingShader.SetTextureFromGlobal(FinalizeKernel, "SpecularGBuffer", "_CameraGBufferTexture1");

            ShadingShader.SetTextureFromGlobal(TTtoOIDNKernel, "DiffuseGBuffer", "_CameraGBufferTexture0");
            ShadingShader.SetTextureFromGlobal(TTtoOIDNKernel, "SpecularGBuffer", "_CameraGBufferTexture1");
            ShadingShader.SetTextureFromGlobal(TTtoOIDNKernel, "NormalTexture", "_CameraGBufferTexture2");

            if (SkyboxTexture == null) SkyboxTexture = new Texture2D(1,1, TextureFormat.RGBA32, false);
            if (SkyboxTexture != null)
            {
                if(CDFTotalBuffer == null) {
                    CDFTotalBuffer = new ComputeBuffer(1, 4);
                }
                if(BackgroundType == 1 && CDFX == null) {
                    CDFX.ReleaseSafe();
                    CDFY.ReleaseSafe();
                    CDFCompute = Resources.Load<ComputeShader>("Utility/CDFCreator");
                    CommonFunctions.CreateRenderTexture(ref CDFX, SkyboxTexture.width, SkyboxTexture.height, CommonFunctions.RTFull1);
                    CommonFunctions.CreateRenderTexture(ref CDFY, SkyboxTexture.height, 1, CommonFunctions.RTFull1);
                    float[] CDFTotalInit = new float[1];
                    CDFTotalBuffer.SetData(CDFTotalInit);
                    ComputeBuffer CounterBuffer = new ComputeBuffer(1, 4);
                    int[] CounterInit = new int[1];
                    CounterBuffer.SetData(CounterInit);
                    CDFCompute.SetTexture(0, "Tex", SkyboxTexture);
                    CDFCompute.SetTexture(0, "CDFX", CDFX);
                    CDFCompute.SetTexture(0, "CDFY", CDFY);
                    CDFCompute.SetInt("w", SkyboxTexture.width);
                    CDFCompute.SetInt("h", SkyboxTexture.height);
                    CDFCompute.SetBuffer(0, "CounterBuffer", CounterBuffer);
                    CDFCompute.SetBuffer(0, "TotalBuff", CDFTotalBuffer);
                    CDFCompute.Dispatch(0, 1, SkyboxTexture.height, 1);
                    CounterBuffer.Release();
                    HDRIParams = new Vector2(SkyboxTexture.width, SkyboxTexture.height);
                    ShadingShader.SetTexture(ShadeKernel, "CDFX", CDFX);
                    ShadingShader.SetTexture(ShadeKernel, "CDFY", CDFY);
                }
                if(BackgroundType != 1) {
                    ShadingShader.SetTexture(ShadeKernel, "CDFX", SkyboxTexture);
                    ShadingShader.SetTexture(ShadeKernel, "CDFY", SkyboxTexture);                    
                } else {
                    ShadingShader.SetTexture(ShadeKernel, "CDFX", CDFX);
                    ShadingShader.SetTexture(ShadeKernel, "CDFY", CDFY);
                }
                ShadingShader.SetBuffer(ShadeKernel, "TotSum", CDFTotalBuffer);
                ShadingShader.SetTexture(ShadeKernel, "_SkyboxTexture", SkyboxTexture);
            }
            SetVector("HDRIParams", HDRIParams);


            ShadingShader.SetTexture(CorrectedDistanceKernel, "CorrectedDepthTex", FlipFrame ? CorrectedDistanceTex : CorrectedDistanceTexB);

            GenerateShader.SetBuffer(GenASVGFKernel, "Rays", (FramesSinceStart2 % 2 == 0) ? RaysBuffer : RaysBufferB);
            GenerateShader.SetTexture(GenASVGFKernel, "RandomNums", FlipFrame ? _RandomNums : _RandomNumsB);
            GenerateShader.SetComputeBuffer(GenASVGFKernel, "GlobalRays", _RayBuffer);
            GenerateShader.SetComputeBuffer(GenASVGFKernel, "GlobalColors", LightingBuffer);
            GenerateShader.SetTexture(GenASVGFKernel, "WorldPosA", GIWorldPosA);
            GenerateShader.SetTexture(GenASVGFKernel, "NEEPosA", FlipFrame ? GINEEPosA : GINEEPosB);
            GenerateShader.SetComputeBuffer(GenASVGFKernel, "GlobalRays", _RayBuffer);
            GenerateShader.SetComputeBuffer(GenASVGFKernel, "GlobalColors", LightingBuffer);


            GenerateShader.SetTexture(GenKernel, "RandomNums", (FramesSinceStart2 % 2 == 0) ? _RandomNums : _RandomNumsB);
            GenerateShader.SetComputeBuffer(GenKernel, "GlobalRays", _RayBuffer);
            GenerateShader.SetComputeBuffer(GenKernel, "GlobalColors", LightingBuffer);            

            AssetManager.Assets.SetMeshTraceBuffers(IntersectionShader, TraceKernel);
            IntersectionShader.SetComputeBuffer(TraceKernel, "GlobalRays", _RayBuffer);
            IntersectionShader.SetComputeBuffer(TraceKernel, "GlobalColors", LightingBuffer);
            IntersectionShader.SetTexture(TraceKernel, "VideoTex", Assets.VideoTexture);
            IntersectionShader.SetTexture(TraceKernel, "_PrimaryTriangleInfo", _PrimaryTriangleInfo);


            AssetManager.Assets.SetMeshTraceBuffers(IntersectionShader, ShadowKernel);
            IntersectionShader.SetComputeBuffer(ShadowKernel, "ShadowRaysBuffer", _ShadowBuffer);
            IntersectionShader.SetComputeBuffer(ShadowKernel, "GlobalColors", LightingBuffer);
            IntersectionShader.SetTexture(ShadowKernel, "VideoTex", Assets.VideoTexture);
            IntersectionShader.SetTexture(ShadowKernel, "NEEPosA", FlipFrame ? GINEEPosA : GINEEPosB);


            AssetManager.Assets.SetHeightmapTraceBuffers(IntersectionShader, HeightmapKernel);
            IntersectionShader.SetComputeBuffer(HeightmapKernel, "GlobalRays", _RayBuffer);
            IntersectionShader.SetTexture(HeightmapKernel, "_PrimaryTriangleInfo", _PrimaryTriangleInfo);

            AssetManager.Assets.SetHeightmapTraceBuffers(IntersectionShader, HeightmapShadowKernel);
            IntersectionShader.SetComputeBuffer(HeightmapShadowKernel, "GlobalColors", LightingBuffer);
            IntersectionShader.SetComputeBuffer(HeightmapShadowKernel, "ShadowRaysBuffer", _ShadowBuffer);
            IntersectionShader.SetTexture(HeightmapShadowKernel, "NEEPosA", FlipFrame ? GINEEPosA : GINEEPosB);


            #if !UseRadianceCache
                GenerateShader.SetComputeBuffer(ResolveKernel, "VoxelDataBufferA", FlipFrame ? VoxelDataBufferA : VoxelDataBufferB);
                GenerateShader.SetComputeBuffer(ResolveKernel, "VoxelDataBufferB", !FlipFrame ? VoxelDataBufferA : VoxelDataBufferB);
                GenerateShader.SetComputeBuffer(ResolveKernel, "HashEntriesBuffer", HashBuffer);
                GenerateShader.SetComputeBuffer(ResolveKernel, "HashEntriesBufferB", HashBuffer);
                GenerateShader.SetComputeBuffer(ResolveKernel + 1, "HashEntriesBuffer", HashBuffer);
                GenerateShader.SetComputeBuffer(ResolveKernel + 2, "VoxelDataBufferA", FlipFrame ? VoxelDataBufferA : VoxelDataBufferB);
                GenerateShader.SetComputeBuffer(ResolveKernel + 2, "HashEntriesBufferB", HashBuffer);
                GenerateShader.SetComputeBuffer(ResolveKernel + 2, "HashEntriesBuffer", HashBufferB);


                IntersectionShader.SetComputeBuffer(ShadowKernel, "CacheBuffer", CacheBuffer);
                IntersectionShader.SetComputeBuffer(HeightmapShadowKernel, "CacheBuffer", CacheBuffer);
                GenerateShader.SetComputeBuffer(GIReTraceKernel, "CacheBuffer", CacheBuffer);
                GenerateShader.SetComputeBuffer(GenASVGFKernel, "CacheBuffer", CacheBuffer);
                GenerateShader.SetComputeBuffer(GenASVGFKernel, "VoxelDataBufferA", FlipFrame ? VoxelDataBufferA : VoxelDataBufferB);
                GenerateShader.SetComputeBuffer(GenKernel, "CacheBuffer", CacheBuffer);
                GenerateShader.SetComputeBuffer(GenKernel, "VoxelDataBufferA", FlipFrame ? VoxelDataBufferA : VoxelDataBufferB);
                
                ShadingShader.SetComputeBuffer(ShadeKernel, "CacheBuffer", CacheBuffer);
                ShadingShader.SetComputeBuffer(ShadeKernel, "HashEntriesBuffer", HashBuffer);
                ShadingShader.SetComputeBuffer(ShadeKernel, "HashEntriesBufferB", HashBufferB);
                ShadingShader.SetComputeBuffer(ShadeKernel, "VoxelDataBufferA", FlipFrame ? VoxelDataBufferA : VoxelDataBufferB);
                ShadingShader.SetComputeBuffer(ShadeKernel, "VoxelDataBufferB", !FlipFrame ? VoxelDataBufferA : VoxelDataBufferB);
                
                ShadingShader.SetComputeBuffer(FinalizeKernel, "CacheBuffer", CacheBuffer);
                ShadingShader.SetComputeBuffer(FinalizeKernel, "HashEntriesBufferB", HashBuffer);
                ShadingShader.SetComputeBuffer(FinalizeKernel, "VoxelDataBufferA", !FlipFrame ? VoxelDataBufferA : VoxelDataBufferB);
                ShadingShader.SetComputeBuffer(FinalizeKernel, "VoxelDataBufferB", FlipFrame ? VoxelDataBufferA : VoxelDataBufferB);
            #endif

            Atmo.AssignTextures(ShadingShader, ShadeKernel);
            AssetManager.Assets.SetLightData(ShadingShader, ShadeKernel);
            AssetManager.Assets.SetMeshTraceBuffers(ShadingShader, ShadeKernel);
            AssetManager.Assets.SetHeightmapTraceBuffers(ShadingShader, ShadeKernel);
            ShadingShader.SetTexture(ShadeKernel, "WorldPosB", !FlipFrame ? GIWorldPosB : GIWorldPosC);
            ShadingShader.SetTexture(ShadeKernel, "NEEPosA", FlipFrame ? GINEEPosA : GINEEPosB);
            ShadingShader.SetTexture(ShadeKernel, "RandomNums", FlipFrame ? _RandomNums : _RandomNumsB);
            ShadingShader.SetTexture(ShadeKernel, "SingleComponentAtlas", Assets.SingleComponentAtlas);
            ShadingShader.SetTexture(ShadeKernel, "_EmissiveAtlas", Assets.EmissiveAtlas);
            ShadingShader.SetTexture(ShadeKernel, "_NormalAtlas", Assets.NormalAtlas);
            ShadingShader.SetTexture(ShadeKernel, "VideoTex", Assets.VideoTexture);
            ShadingShader.SetTexture(ShadeKernel, "ScreenSpaceInfo", FlipFrame ? ScreenSpaceInfo : ScreenSpaceInfoPrev);
            ShadingShader.SetComputeBuffer(ShadeKernel, "GlobalColors", LightingBuffer);
            ShadingShader.SetComputeBuffer(ShadeKernel, "GlobalRays", _RayBuffer);
            ShadingShader.SetComputeBuffer(ShadeKernel, "ShadowRaysBuffer", _ShadowBuffer);


            ShadingShader.SetBuffer(FinalizeKernel, "GlobalColors", LightingBuffer);
            ShadingShader.SetTexture(FinalizeKernel, "Result", _target);

            #if UseOIDN
                ShadingShader.SetBuffer(TTtoOIDNKernel, "AlbedoBuffer", AlbedoBuffer);
                ShadingShader.SetBuffer(TTtoOIDNKernel, "NormalBuffer", NormalBuffer);
            #endif

            GenerateShader.SetComputeBuffer(GIReTraceKernel, "GlobalRays", _RayBuffer);
            GenerateShader.SetComputeBuffer(GIReTraceKernel, "GlobalColors", LightingBuffer);
            GenerateShader.SetTexture(GIReTraceKernel, "WorldPosA", GIWorldPosA);
            GenerateShader.SetTexture(GIReTraceKernel, "NEEPosA", FlipFrame ? GINEEPosA : GINEEPosB);
            GenerateShader.SetComputeBuffer(GIReTraceKernel, "PrevGlobalColorsA", FlipFrame ? PrevLightingBufferA : PrevLightingBufferB);
            GenerateShader.SetBuffer(GIReTraceKernel, "Rays", FlipFrame ? RaysBuffer : RaysBufferB);
            GenerateShader.SetTexture(GIReTraceKernel, "RandomNumsWrite", FlipFrame ? _RandomNums : _RandomNumsB);
            GenerateShader.SetTexture(GIReTraceKernel, "ReservoirA", !FlipFrame ? GIReservoirB : GIReservoirC);
            GenerateShader.SetTexture(GIReTraceKernel, "RandomNums", !FlipFrame ? _RandomNums : _RandomNumsB);
            GenerateShader.SetTexture(GIReTraceKernel, "ScreenSpaceInfo", !FlipFrame ? ScreenSpaceInfo : ScreenSpaceInfoPrev);
            

            AssetManager.Assets.SetMeshTraceBuffers(ReSTIRGI, ReSTIRGIKernel);
            ReSTIRGI.SetTexture(ReSTIRGIKernel, "ReservoirA", FlipFrame ? GIReservoirB : GIReservoirC);
            ReSTIRGI.SetTexture(ReSTIRGIKernel, "ReservoirB", !FlipFrame ? GIReservoirB : GIReservoirC);
            ReSTIRGI.SetTexture(ReSTIRGIKernel, "WorldPosC", GIWorldPosA);
            ReSTIRGI.SetTexture(ReSTIRGIKernel, "WorldPosA", FlipFrame ? GIWorldPosB : GIWorldPosC);
            ReSTIRGI.SetTexture(ReSTIRGIKernel, "WorldPosB", !FlipFrame ? GIWorldPosB : GIWorldPosC);
            ReSTIRGI.SetTexture(ReSTIRGIKernel, "NEEPosA", FlipFrame ? GINEEPosA : GINEEPosB);
            ReSTIRGI.SetTexture(ReSTIRGIKernel, "NEEPosB", !FlipFrame ? GINEEPosA : GINEEPosB);
            ReSTIRGI.SetTexture(ReSTIRGIKernel, "PrevScreenSpaceInfo", FlipFrame ? ScreenSpaceInfoPrev : ScreenSpaceInfo);
            ReSTIRGI.SetTexture(ReSTIRGIKernel, "RandomNums", FlipFrame ? _RandomNums : _RandomNumsB);
            ReSTIRGI.SetTexture(ReSTIRGIKernel, "ScreenSpaceInfoRead", FlipFrame ? ScreenSpaceInfo : ScreenSpaceInfoPrev);
            ReSTIRGI.SetTexture(ReSTIRGIKernel, "GradientWrite", Gradients);
            ReSTIRGI.SetTexture(ReSTIRGIKernel, "PrimaryTriData", _PrimaryTriangleInfo);
            ReSTIRGI.SetComputeBuffer(ReSTIRGIKernel, "PrevGlobalColorsA", FlipFrame ? PrevLightingBufferA : PrevLightingBufferB);
            ReSTIRGI.SetComputeBuffer(ReSTIRGIKernel, "PrevGlobalColorsB", FlipFrame ? PrevLightingBufferB : PrevLightingBufferA);
            ReSTIRGI.SetComputeBuffer(ReSTIRGIKernel, "GlobalColors", LightingBuffer);

            AssetManager.Assets.SetMeshTraceBuffers(ReSTIRGI, ReSTIRGISpatialKernel);
            ReSTIRGI.SetComputeBuffer(ReSTIRGISpatialKernel, "GlobalColors", LightingBuffer);
            ReSTIRGI.SetTexture(ReSTIRGISpatialKernel, "RandomNums", FlipFrame ? _RandomNums : _RandomNumsB);
            ReSTIRGI.SetTexture(ReSTIRGISpatialKernel, "ScreenSpaceInfoRead", FlipFrame ? ScreenSpaceInfo : ScreenSpaceInfoPrev);
            ReSTIRGI.SetTexture(ReSTIRGISpatialKernel, "PrimaryTriData", _PrimaryTriangleInfo);

            Shader.SetGlobalTexture("_DebugTex", _DebugTex);
        }

        private void ResetAllTextures() {
            // _camera.renderingPath = RenderingPath.DeferredShading;
            _camera.depthTextureMode |= DepthTextureMode.MotionVectors;
            if(PrevResFactor != RenderScale || TargetWidth != _camera.scaledPixelWidth) {
                TargetWidth = _camera.scaledPixelWidth;
                TargetHeight = _camera.scaledPixelHeight;
                SourceWidth = (int)Mathf.Ceil((float)TargetWidth * RenderScale);
                SourceHeight = (int)Mathf.Ceil((float)TargetHeight * RenderScale);
                if (Mathf.Abs(SourceWidth - TargetWidth) < 2)
                {
                    SourceWidth = TargetWidth;
                    SourceHeight = TargetHeight;
                    RenderScale = 1;
                }
                PrevResFactor = RenderScale;
                if(UseASVGF && UseReSTIRGI) {ReSTIRASVGFCode.ClearAll(); ReSTIRASVGFCode.init(SourceWidth, SourceHeight);}
                if (UseASVGF && !UseReSTIRGI) {ASVGFCode.ClearAll(); ASVGFCode.init(SourceWidth, SourceHeight);}
                if(UseReCur) {ReCurDen.ClearAll(); ReCurDen.init(SourceWidth, SourceHeight);}
                if(Denoisers.Initialized) Denoisers.ClearAll();
                Denoisers.init(SourceWidth, SourceHeight);

                InitRenderTexture(true);
                CommonFunctions.CreateDynamicBuffer(ref _RayBuffer, SourceWidth * SourceHeight * 2, 48);
                CommonFunctions.CreateDynamicBuffer(ref _ShadowBuffer, SourceWidth * SourceHeight, 48);
                CommonFunctions.CreateDynamicBuffer(ref LightingBuffer, SourceWidth * SourceHeight, 64);
                CommonFunctions.CreateDynamicBuffer(ref PrevLightingBufferA, SourceWidth * SourceHeight, 64);
                CommonFunctions.CreateDynamicBuffer(ref PrevLightingBufferB, SourceWidth * SourceHeight, 64);
                CommonFunctions.CreateDynamicBuffer(ref RaysBuffer, SourceWidth * SourceHeight, 24);
                CommonFunctions.CreateDynamicBuffer(ref RaysBufferB, SourceWidth * SourceHeight, 24);
                CommonFunctions.CreateRenderTexture(ref _RandomNums, SourceWidth, SourceHeight, CommonFunctions.RTFull4);
                CommonFunctions.CreateRenderTexture(ref _RandomNumsB, SourceWidth, SourceHeight, CommonFunctions.RTFull4);
                #if !UseRadianceCache
                    CommonFunctions.CreateDynamicBuffer(ref CacheBuffer, SourceWidth * SourceHeight, 48);
                    CommonFunctions.CreateDynamicBuffer(ref VoxelDataBufferA, 4 * 1024 * 1024 * 4, 4, ComputeBufferType.Raw);
                    CommonFunctions.CreateDynamicBuffer(ref VoxelDataBufferB, 4 * 1024 * 1024 * 4, 4, ComputeBufferType.Raw);
                    CommonFunctions.CreateDynamicBuffer(ref HashBuffer, 4 * 1024 * 1024, 12);
                    CommonFunctions.CreateDynamicBuffer(ref HashBufferB, 4 * 1024 * 1024, 12);

                    int[] EEE = new int[4 * 1024 * 1024 * 3];
                    HashBuffer.SetData(EEE);
                    HashBufferB.SetData(EEE);
                    int[] EEEE = new int[4 * 1024 * 1024 * 4];
                    VoxelDataBufferB.SetData(EEEE);
                    VoxelDataBufferA.SetData(EEEE);
                #endif
            }
            PrevResFactor = RenderScale;
        }


        private void InitRenderTexture(bool ForceReset = false)
        {
            if (ForceReset || _target == null || _target.width != SourceWidth || _target.height != SourceHeight)
            {
                // Release render texture if we already have one
                if (_target != null)
                {
                    _target.Release();
                    _converged.Release();
                    _DebugTex.Release();
                    _FinalTex.Release();
                    CorrectedDistanceTex.Release();
                    CorrectedDistanceTexB.Release();
                    GIReservoirA.Release();
                    GIReservoirB.Release();
                    GIReservoirC.Release();
                    GINEEPosA.Release();
                    GINEEPosB.Release();
                    GINEEPosC.Release();
                    GIWorldPosA.Release();
                    GIWorldPosB.Release();
                    GIWorldPosC.Release();
                    _PrimaryTriangleInfo.Release();
                    ScreenSpaceInfo.Release();
                    ScreenSpaceInfoPrev.Release();
                    Gradients.Release();
                    _RandomNums.Release();
                    _RandomNumsB.Release();
                    #if UseOIDN
                        ColorBuffer.Release();
                        OutputBuffer.Release();
                        AlbedoBuffer.Release();
                        NormalBuffer.Release();
                        OIDNDenoiser.Dispose();
                    #endif
                }

                #if UseOIDN
                    UnityDenoiserPlugin.DenoiserConfig cfg = new UnityDenoiserPlugin.DenoiserConfig() {
                        imageWidth = SourceWidth,
                        imageHeight = SourceHeight,
                        guideAlbedo = 1,
                        guideNormal = 1,
                        temporalMode = 0,
                        cleanAux = 1,
                        prefilterAux = 0
                    };
                    OIDNDenoiser = new UnityDenoiserPlugin.DenoiserPluginWrapper(UnityDenoiserPlugin.DenoiserType.OIDN, cfg);
                    ColorBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, SourceWidth * SourceHeight, 12);
                    OutputBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, SourceWidth * SourceHeight, 12);
                    AlbedoBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, SourceWidth * SourceHeight, 12);
                    NormalBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, SourceWidth * SourceHeight, 12);
                #endif

                CommonFunctions.CreateRenderTexture(ref _RandomNums, SourceWidth, SourceHeight, CommonFunctions.RTFull4);
                CommonFunctions.CreateRenderTexture(ref _RandomNumsB, SourceWidth, SourceHeight, CommonFunctions.RTFull4);
                CommonFunctions.CreateRenderTexture(ref _DebugTex, SourceWidth, SourceHeight, CommonFunctions.RTFull4, RenderTextureReadWrite.sRGB);
                CommonFunctions.CreateRenderTexture(ref _FinalTex, TargetWidth, TargetHeight, CommonFunctions.RTFull4, RenderTextureReadWrite.sRGB, true);
                CommonFunctions.CreateRenderTexture(ref _target, SourceWidth, SourceHeight, CommonFunctions.RTHalf4, RenderTextureReadWrite.sRGB);
                CommonFunctions.CreateRenderTexture(ref _converged, SourceWidth, SourceHeight, CommonFunctions.RTFull4, RenderTextureReadWrite.sRGB);
                CommonFunctions.CreateRenderTexture(ref CorrectedDistanceTex, SourceWidth, SourceHeight, CommonFunctions.RTHalf2);
                CommonFunctions.CreateRenderTexture(ref CorrectedDistanceTexB, SourceWidth, SourceHeight, CommonFunctions.RTHalf2);
                CommonFunctions.CreateRenderTexture(ref GIReservoirA, SourceWidth, SourceHeight, CommonFunctions.RTHalf4);
                CommonFunctions.CreateRenderTexture(ref GIReservoirB, SourceWidth, SourceHeight, CommonFunctions.RTHalf4);
                CommonFunctions.CreateRenderTexture(ref GIReservoirC, SourceWidth, SourceHeight, CommonFunctions.RTHalf4);
                CommonFunctions.CreateRenderTexture(ref GINEEPosA, SourceWidth, SourceHeight, CommonFunctions.RTHalf4);
                CommonFunctions.CreateRenderTexture(ref GINEEPosB, SourceWidth, SourceHeight, CommonFunctions.RTHalf4);
                CommonFunctions.CreateRenderTexture(ref GINEEPosC, SourceWidth, SourceHeight, CommonFunctions.RTHalf4);
                CommonFunctions.CreateRenderTexture(ref GIWorldPosA, SourceWidth, SourceHeight, CommonFunctions.RTFull4);
                CommonFunctions.CreateRenderTexture(ref GIWorldPosB, SourceWidth, SourceHeight, CommonFunctions.RTFull4);
                CommonFunctions.CreateRenderTexture(ref GIWorldPosC, SourceWidth, SourceHeight, CommonFunctions.RTFull4);
                CommonFunctions.CreateRenderTexture(ref _PrimaryTriangleInfo, SourceWidth, SourceHeight, CommonFunctions.RTFull4);
                CommonFunctions.CreateRenderTexture(ref ScreenSpaceInfo, SourceWidth, SourceHeight, CommonFunctions.RTFull4);
                CommonFunctions.CreateRenderTexture(ref ScreenSpaceInfoPrev, SourceWidth, SourceHeight, CommonFunctions.RTFull4);
                CommonFunctions.CreateRenderTexture(ref Gradients, SourceWidth / 3, SourceHeight / 3, CommonFunctions.RTHalf2);
                #if TTLightMapping
                    CommonFunctions.CreateRenderTexture(ref LightWorldIndex, SourceWidth, SourceHeight, CommonFunctions.RTFull4);
                #endif
                // Reset sampling
                _currentSample = 0;
                uFirstFrame = 1;
                FramesSinceStart = 0;
                FramesSinceStart2 = 0;
            }
        }
        public void ClearOutRenderTexture(RenderTexture renderTexture)
        {
            RenderTexture rt = RenderTexture.active;
            RenderTexture.active = renderTexture;
            GL.Clear(true, true, Color.clear);
            RenderTexture.active = rt;
        }
        public Vector2[] Spatials;
        private void Render(RenderTexture destination, CommandBuffer cmd)
        {
            #if !UseRadianceCache
                cmd.DispatchCompute(GenerateShader, ResolveKernel + 2, Mathf.CeilToInt((4.0f * 1024.0f * 1024.0f) / 256.0f), 1, 1);
            #endif
            if(Spatials == null || Spatials.Length == 0) {Spatials = new Vector2[1]; Spatials[0] = new Vector2(7,30);}
            Denoisers.ValidateInit(AllowBloom, AllowTAA, SourceWidth != TargetWidth, UseTAAU, DoSharpen);
            float CurrentSample;
            cmd.BeginSample("Linearize and Copy Depth");
            cmd.DispatchCompute(ShadingShader, CorrectedDistanceKernel, Mathf.CeilToInt(SourceWidth / 32.0f), Mathf.CeilToInt(SourceHeight / 32.0f), 1);
            cmd.EndSample("Linearize and Copy Depth");
            
            if (UseASVGF && !UseReSTIRGI) {
                cmd.BeginSample("ASVGF Reproject Pass");
                ASVGFCode.shader.SetBool("ReSTIRGI", UseReSTIRGI);
                ASVGFCode.DoRNG(ref _RandomNums, ref _RandomNumsB, FramesSinceStart2, ref RaysBuffer, ref RaysBufferB, (FramesSinceStart2 % 2 == 1) ? CorrectedDistanceTex : CorrectedDistanceTexB, cmd, (FramesSinceStart2 % 2 == 0) ? CorrectedDistanceTex : CorrectedDistanceTexB, _PrimaryTriangleInfo, AssetManager.Assets.MeshDataBuffer, Assets.AggTriBuffer, MeshOrderChanged, Assets.TLASCWBVHIndexes);
                GenerateShader.SetBuffer(GenASVGFKernel, "Rays", (FramesSinceStart2 % 2 == 0) ? RaysBuffer : RaysBufferB);
                ASVGFCode.shader.SetTexture(1, "ScreenSpaceInfoWrite", (FramesSinceStart2 % 2 == 0) ? ScreenSpaceInfo : ScreenSpaceInfoPrev);
                cmd.EndSample("ASVGF Reproject Pass");
            }

            SetInt("CurBounce", 0, cmd);
            #if TTLightMapping
                cmd.BeginSample("Clear");
                cmd.DispatchCompute(GenerateShader, GenLightmapKernel + 1, Mathf.CeilToInt(SourceWidth / 16.0f), Mathf.CeilToInt(SourceHeight / 16.0f), 1);
                cmd.EndSample("Clear");
                if(_currentSample == 0) {
                    cmd.BeginSample("Gen2");
                    cmd.DispatchCompute(GenerateShader, GenLightmapKernel + 2, Mathf.CeilToInt(LightMapTrisBuffer.count / 256.0f), 1, 1);
                    cmd.EndSample("Gen2");
                }                  
                cmd.BeginSample("Gen");
                cmd.DispatchCompute(GenerateShader, GenLightmapKernel, Mathf.CeilToInt(SourceWidth / 16.0f), Mathf.CeilToInt(SourceHeight / 16.0f), 1);
                cmd.EndSample("Gen");
            #else
                if(UseReSTIRGI && ReSTIRGIUpdateRate != 0) {
                    cmd.BeginSample("ReSTIR GI Reproject");
                    cmd.DispatchCompute(GenerateShader, GIReTraceKernel, Mathf.CeilToInt(SourceWidth / 16.0f), Mathf.CeilToInt(SourceHeight / 16.0f), 1);
                    cmd.EndSample("ReSTIR GI Reproject");
                } else {
                    cmd.BeginSample("Primary Ray Generation");
                    cmd.DispatchCompute(GenerateShader, (UseASVGF && !UseReSTIRGI) ? GenASVGFKernel : GenKernel, Mathf.CeilToInt(SourceWidth / 256.0f), SourceHeight, 1);
                    cmd.EndSample("Primary Ray Generation");
                }
            #endif

                cmd.BeginSample("Pathtracing Kernels");

                for (int i = 0; i < bouncecount; i++) {
                    cmd.BeginSample("Bounce: " + i);
                        var bouncebounce = i;
                        if(bouncebounce == 1) cmd.SetComputeTextureParam(IntersectionShader, TraceKernel, "_PrimaryTriangleInfo", GIWorldPosA);
                        SetInt("CurBounce", bouncebounce, cmd);
                        cmd.BeginSample("Transfer Kernel: " + i);
                        cmd.SetComputeIntParam(ShadingShader, "Type", 0);
                        cmd.DispatchCompute(ShadingShader, TransferKernel, 1, 1, 1);
                        cmd.EndSample("Transfer Kernel: " + i);

                        cmd.BeginSample("Trace Kernel: " + i);
                        #if DX11Only
                            cmd.DispatchCompute(IntersectionShader, TraceKernel, Mathf.CeilToInt((SourceHeight * SourceWidth) / 64.0f), 1, 1);
                        #else
                            cmd.DispatchCompute(IntersectionShader, TraceKernel, CurBounceInfoBuffer, 0);//784 is 28^2
                        #endif
                        cmd.EndSample("Trace Kernel: " + i);


                        if (Assets.Terrains.Count != 0) {
                            cmd.BeginSample("HeightMap Trace Kernel: " + i);
                            cmd.DispatchCompute(IntersectionShader, HeightmapKernel, 784, 1, 1);
                            cmd.EndSample("HeightMap Trace Kernel: " + i);
                        }

                        cmd.BeginSample("Shading Kernel: " + i);
                        #if DX11Only
                            cmd.DispatchCompute(ShadingShader, ShadeKernel, Mathf.CeilToInt((SourceHeight * SourceWidth) / 64.0f), 1, 1);
                        #else
                            cmd.DispatchCompute(ShadingShader, ShadeKernel, CurBounceInfoBuffer, 0);
                        #endif
                        cmd.EndSample("Shading Kernel: " + i);

                        cmd.BeginSample("Transfer Kernel 2: " + i);
                        cmd.SetComputeIntParam(ShadingShader, "Type", 1);
                        cmd.DispatchCompute(ShadingShader, TransferKernel, 1, 1, 1);
                        cmd.EndSample("Transfer Kernel 2: " + i);
                        if (UseNEE) {
                            cmd.BeginSample("Shadow Kernel: " + i);
                            #if DX11Only
                                cmd.DispatchCompute(IntersectionShader, ShadowKernel, Mathf.CeilToInt((SourceHeight * SourceWidth) / 64.0f), 1, 1);
                            #else
                                cmd.DispatchCompute(IntersectionShader, ShadowKernel, CurBounceInfoBuffer, 0);
                            #endif
                            cmd.EndSample("Shadow Kernel: " + i);
                        }
                        if (UseNEE && Assets.Terrains.Count != 0) {
                            cmd.BeginSample("Heightmap Shadow Kernel: " + i);
                            cmd.DispatchCompute(IntersectionShader, HeightmapShadowKernel, 784, 1, 1);
                            cmd.EndSample("Heightmap Shadow Kernel: " + i);
                        }
                    cmd.EndSample("Bounce: " + i);

                }
            cmd.EndSample("Pathtracing Kernels");

            #if !UseRadianceCache
                cmd.BeginSample("RadCache Resolve Kernel");
                    cmd.DispatchCompute(GenerateShader, ResolveKernel, Mathf.CeilToInt((4.0f * 1024.0f * 1024.0f) / 256.0f), 1, 1);
                cmd.EndSample("RadCache Resolve Kernel");    
                cmd.BeginSample("RadCache Copy Kernel");
                    cmd.DispatchCompute(GenerateShader, ResolveKernel + 1, Mathf.CeilToInt((4.0f * 1024.0f * 1024.0f) / 256.0f), 1, 1);
                cmd.EndSample("RadCache Copy Kernel");
            #endif


            if (UseReSTIRGI) {
                SetInt("CurBounce", 0, cmd);
                cmd.BeginSample("ReSTIRGI Temporal Kernel");
                cmd.DispatchCompute(ReSTIRGI, ReSTIRGIKernel, Mathf.CeilToInt(SourceWidth / 16.0f), Mathf.CeilToInt(SourceHeight / 16.0f), 1);
                cmd.EndSample("ReSTIRGI Temporal Kernel");

                cmd.BeginSample("ReSTIRGI Spatial Kernel 0");
                cmd.SetComputeIntParam(ReSTIRGI, "Finish", -1);
                cmd.SetComputeIntParam(ReSTIRGI, "RandOffset", 1);
                bool FlipFrame = (FramesSinceStart2 % 2 == 0);
                cmd.SetComputeBufferParam(ReSTIRGI, ReSTIRGISpatialKernel, "PrevGlobalColorsA", FlipFrame ? PrevLightingBufferB : PrevLightingBufferA);
                cmd.SetComputeTextureParam(ReSTIRGI, ReSTIRGISpatialKernel, "WorldPosB", FlipFrame ? GIWorldPosB : GIWorldPosC);
                cmd.SetComputeTextureParam(ReSTIRGI, ReSTIRGISpatialKernel, "NEEPosB", FlipFrame ? GINEEPosA : GINEEPosB);
                cmd.SetComputeTextureParam(ReSTIRGI, ReSTIRGISpatialKernel, "ReservoirB", FlipFrame ? GIReservoirB : GIReservoirC);

                cmd.SetComputeBufferParam(ReSTIRGI, ReSTIRGISpatialKernel, "GlobalColors", !FlipFrame ? PrevLightingBufferB : PrevLightingBufferA);
                cmd.SetComputeTextureParam(ReSTIRGI, ReSTIRGISpatialKernel, "ReservoirA", GIReservoirA);
                cmd.SetComputeTextureParam(ReSTIRGI, ReSTIRGISpatialKernel, "WorldPosA", !FlipFrame ? GIWorldPosB : GIWorldPosC);
                cmd.SetComputeTextureParam(ReSTIRGI, ReSTIRGISpatialKernel, "NEEPosA", !FlipFrame ? GINEEPosA : GINEEPosB);
                
                cmd.DispatchCompute(ReSTIRGI, ReSTIRGISpatialKernel, Mathf.CeilToInt(SourceWidth / 16.0f), Mathf.CeilToInt(SourceHeight / 16.0f), 1);
                cmd.SetComputeIntParam(ReSTIRGI, "Finish", 0);
                cmd.EndSample("ReSTIRGI Spatial Kernel 0");

                int Len = (int)Mathf.Floor(Spatials.Length / 2.0f) * 2;
                for(int i = 0; i <= Len; i++) {
                    bool IFlip = i % 2 == 0;
                    Vector2 Spatial = Spatials[Mathf.Min(i,Spatials.Length - 1)];
                    var II = i;
                    cmd.SetComputeIntParam(ReSTIRGI, "RandOffset", II + 2);
                    if(i == Len) cmd.SetComputeIntParam(ReSTIRGI, "Finish", 1);
                    cmd.SetComputeFloatParam(ReSTIRGI, "GISpatialRadius", Spatial.y);
                    cmd.SetComputeIntParam(ReSTIRGI, "ReSTIRGISpatialCount", (int)Spatial.x);
                    cmd.SetComputeBufferParam(ReSTIRGI, ReSTIRGISpatialKernel, "PrevGlobalColorsA", IFlip ? (!FlipFrame ? PrevLightingBufferB : PrevLightingBufferA) : LightingBuffer);
                    cmd.SetComputeTextureParam(ReSTIRGI, ReSTIRGISpatialKernel, "WorldPosB", IFlip ? (!FlipFrame ? GIWorldPosB : GIWorldPosC) : GIWorldPosA);
                    cmd.SetComputeTextureParam(ReSTIRGI, ReSTIRGISpatialKernel, "NEEPosB", IFlip ? (!FlipFrame ? GINEEPosA : GINEEPosB) : GINEEPosC);
                    cmd.SetComputeTextureParam(ReSTIRGI, ReSTIRGISpatialKernel, "ReservoirB", IFlip ? (GIReservoirA) : (!FlipFrame ? GIReservoirB : GIReservoirC));

                    cmd.SetComputeBufferParam(ReSTIRGI, ReSTIRGISpatialKernel, "GlobalColors", !IFlip ? (!FlipFrame ? PrevLightingBufferB : PrevLightingBufferA) : LightingBuffer);
                    cmd.SetComputeTextureParam(ReSTIRGI, ReSTIRGISpatialKernel, "WorldPosA", !IFlip ? (!FlipFrame ? GIWorldPosB : GIWorldPosC) : GIWorldPosA);
                    cmd.SetComputeTextureParam(ReSTIRGI, ReSTIRGISpatialKernel, "NEEPosA", !IFlip ? (!FlipFrame ? GINEEPosA : GINEEPosB) : GINEEPosC);
                    cmd.SetComputeTextureParam(ReSTIRGI, ReSTIRGISpatialKernel, "ReservoirA", !IFlip ? (GIReservoirA) : (!FlipFrame ? GIReservoirB : GIReservoirC));
                    cmd.BeginSample("ReSTIRGI Spatial Kernel " + (i+1));

                    cmd.DispatchCompute(ReSTIRGI, ReSTIRGISpatialKernel, Mathf.CeilToInt(SourceWidth / 16.0f), Mathf.CeilToInt(SourceHeight / 16.0f), 1);
                    cmd.EndSample("ReSTIRGI Spatial Kernel " + (i+1));
                }
            }

            if (!(!UseReSTIRGI && UseASVGF) && !UseReCur && !(UseReSTIRGI && UseASVGF))
            {
                cmd.BeginSample("Finalize Kernel");
                cmd.DispatchCompute(ShadingShader, FinalizeKernel, Mathf.CeilToInt(SourceWidth / 16.0f), Mathf.CeilToInt(SourceHeight / 16.0f), 1);
                CurrentSample = 1.0f / (FramesSinceStart + 1.0f);
                SampleCount++;

                if (_addMaterial == null)
                    _addMaterial = new Material(Shader.Find("Hidden/Accumulate"));
                _addMaterial.SetFloat("_Sample", CurrentSample);
                cmd.Blit(_target, _converged, _addMaterial);
                cmd.EndSample("Finalize Kernel");
            } else if(!UseReCur && !(UseReSTIRGI && UseASVGF)) {
                cmd.BeginSample("ASVGF");
                SampleCount = 0;
                ASVGFCode.Do(ref LightingBuffer, ref _converged, RenderScale, (FramesSinceStart2 % 2 == 1) ? CorrectedDistanceTex : CorrectedDistanceTexB, ((FramesSinceStart2 % 2 == 0) ? ScreenSpaceInfo : ScreenSpaceInfoPrev), cmd, (FramesSinceStart2 % 2 == 0) ? CorrectedDistanceTex : CorrectedDistanceTexB, FramesSinceStart2, ref GIWorldPosA, DoPartialRendering ? PartialRenderingFactor : 1, Denoisers.ExposureBuffer, AllowAutoExpose, IndirectBoost, (FramesSinceStart2 % 2 == 0) ? _RandomNums : _RandomNumsB);
                CurrentSample = 1;
                cmd.EndSample("ASVGF");
            } else if(UseReSTIRGI && UseASVGF && !UseReCur) {
                cmd.BeginSample("ReSTIR ASVGF");
                SampleCount = 0;
                ReSTIRASVGFCode.Do(ref LightingBuffer,
                                    ref _converged, 
                                    RenderScale, 
                                    (FramesSinceStart2 % 2 == 1) ? CorrectedDistanceTex : CorrectedDistanceTexB, 
                                    ((FramesSinceStart2 % 2 == 0) ? ScreenSpaceInfo : ScreenSpaceInfoPrev), 
                                    ((FramesSinceStart2 % 2 == 1) ? ScreenSpaceInfo : ScreenSpaceInfoPrev), 
                                    cmd, 
                                    (FramesSinceStart2 % 2 == 0) ? CorrectedDistanceTex : CorrectedDistanceTexB, 
                                    FramesSinceStart2, 
                                    ref GIWorldPosA, 
                                    DoPartialRendering ? PartialRenderingFactor : 1, 
                                    Denoisers.ExposureBuffer, 
                                    AllowAutoExpose, 
                                    IndirectBoost, 
                                    Gradients,
                                    _PrimaryTriangleInfo, 
                                    AssetManager.Assets.MeshDataBuffer, 
                                    Assets.AggTriBuffer);
                CurrentSample = 1;
                cmd.EndSample("ReSTIR ASVGF");
            } else {
                cmd.BeginSample("ReCur");
                SampleCount = 0;
                ReCurDen.Do(ref _converged, 
                            ref LightingBuffer, 
                            ((FramesSinceStart2 % 2 == 0) ? ScreenSpaceInfo : ScreenSpaceInfoPrev), 
                            CorrectedDistanceTexB, 
                            CorrectedDistanceTex, 
                            ((FramesSinceStart2 % 2 == 0) ? GIReservoirB : GIReservoirC), 
                            ((FramesSinceStart2 % 2 == 1) ? GIReservoirB : GIReservoirC), 
                            GIWorldPosA, 
                            cmd, 
                            FramesSinceStart2, 
                            UseReSTIRGI, 
                            RenderScale, 
                            ReCurBlurRadius, 
                            DoPartialRendering ? PartialRenderingFactor : 1, 
                            IndirectBoost, 
                            Gradients);
                CurrentSample = 1.0f;
                cmd.EndSample("ReCur");
            }
            cmd.BeginSample("Firefly Blit");
            if (_FireFlyMaterial == null)
                _FireFlyMaterial = new Material(Shader.Find("Hidden/FireFlyPass"));
            if(DoFirefly && SampleCount > FireflyFrameCount) {
                _FireFlyMaterial.SetFloat("_Strength", FireflyStrength);
                _FireFlyMaterial.SetFloat("_Offset", FireflyOffset);

                cmd.Blit(_converged, _target, _FireFlyMaterial);
                cmd.Blit(_target, _converged);
            }
            cmd.EndSample("Firefly Blit");


            cmd.BeginSample("Post Processing");
            if (SourceWidth != TargetWidth)
            {
                if (UseTAAU) Denoisers.ExecuteTAAU(ref _FinalTex, ref _converged, cmd, FramesSinceStart2, (FramesSinceStart2 % 2 == 0) ? CorrectedDistanceTex : CorrectedDistanceTexB);
                else Denoisers.ExecuteUpsample(ref _converged, ref _FinalTex, FramesSinceStart2, _currentSample, cmd, ((FramesSinceStart2 % 2 == 0) ? ScreenSpaceInfo : ScreenSpaceInfoPrev));//This is a postprocessing pass, but im treating it like its not one, need to move it to after the accumulation
            }
            else
            {
                cmd.CopyTexture(_converged, 0, 0, _FinalTex, 0, 0);
            }

            if (AllowAutoExpose)
            {
                _FinalTex.GenerateMips();
                Denoisers.ExecuteAutoExpose(ref _FinalTex, Exposure, cmd, DoExposureAuto);
            }

            #if UseOIDN
                if(UseOIDN && SampleCount > OIDNFrameCount) {
                    cmd.SetComputeBufferParam(ShadingShader, TTtoOIDNKernel, "OutputBuffer", ColorBuffer);
                    ShadingShader.SetTexture(TTtoOIDNKernel, "Result", _FinalTex);
                    cmd.DispatchCompute(ShadingShader, TTtoOIDNKernel, Mathf.CeilToInt(SourceWidth / 16.0f), Mathf.CeilToInt(SourceHeight / 16.0f), 1);

                    OIDNDenoiser.Render(cmd, ColorBuffer, OutputBuffer, AlbedoBuffer, NormalBuffer);

                    cmd.SetComputeBufferParam(ShadingShader, OIDNtoTTKernel, "OutputBuffer", OutputBuffer);
                    ShadingShader.SetTexture(OIDNtoTTKernel, "Result", _FinalTex);
                    cmd.DispatchCompute(ShadingShader, OIDNtoTTKernel, Mathf.CeilToInt(SourceWidth / 16.0f), Mathf.CeilToInt(SourceHeight / 16.0f), 1);            
                }
            #endif

            if (AllowBloom) Denoisers.ExecuteBloom(ref _FinalTex, BloomStrength, cmd);
            if(AllowToneMap) Denoisers.ExecuteToneMap(ref _FinalTex, cmd, ref ToneMapTex, ref ToneMapTex2, ToneMapper);
            if (AllowTAA) Denoisers.ExecuteTAA(ref _FinalTex, _currentSample, cmd);
            if (DoSharpen) Denoisers.ExecuteSharpen(ref _FinalTex, Sharpness, cmd);
            cmd.Blit(_FinalTex, destination);
            ClearOutRenderTexture(_DebugTex);
            cmd.EndSample("Post Processing");
            _currentSample++;
            FramesSinceStart++;
            FramesSinceStart2++;
            PrevCamPosition = _camera.transform.position;
            PrevASVGF = UseASVGF;
            PrevReCur = UseReCur;
            PrevReSTIRGI = UseReSTIRGI;
        }

        public void RenderImage(RenderTexture destination, CommandBuffer cmd)
        {
            Abandon = false;
            _camera.renderingPath = RenderingPath.DeferredShading;
            if (SceneIsRunning && Assets != null && Assets.RenderQue.Count > 0)
            {
                #if TTLightMapping
                    if(LightMapTemp != null) Graphics.CopyTexture(LightMapTemp,0,0, lightmaps[CurrentLightmapIndex].lightmapColor,0,0);
                    if(uFirstFrame == 1 || FramesSinceStart2 > LightMappingSampleCount) {
                        if(uFirstFrame == 1) CurrentLightmapIndex = 0;
                        else CurrentLightmapIndex = (CurrentLightmapIndex + 1) % lightmaps.Length;
                        CommonFunctions.CreateComputeBuffer(ref LightMapTrisBuffer, Assets.LightMaps[CurrentLightmapIndex].LightMapTris);
                        FramesSinceStart = 0;
                        FramesSinceStart2 = 0;
                        SampleCount = 0;
                        _currentSample = 0;
                        CommonFunctions.CreateRenderTexture(ref LightMapTemp, lightmaps[CurrentLightmapIndex].lightmapColor.width, lightmaps[CurrentLightmapIndex].lightmapColor.height, CommonFunctions.RTHalf4);
                        Abandon = true;
                    }
                #endif
                ResetAllTextures();
                RunUpdate();
                if(RebuildMeshObjectBuffers(cmd)) {
                    InitRenderTexture();
                    SetShaderParameters(cmd);
                    #if TTLightMapping
                        Render(LightMapTemp, cmd);
                        cmd.Blit(LightMapTemp, destination);
                    #else
                        Render(destination, cmd);
                    #endif
                }
                uFirstFrame = 0;
            }
            else
            {
                try { int throwawayBool = Assets.UpdateTLAS(cmd); _meshObjectsNeedRebuilding = true;} catch (System.IndexOutOfRangeException) { }
            }
            SceneIsRunning = true;
        }
    }
}
