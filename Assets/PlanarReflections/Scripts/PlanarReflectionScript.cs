using System;
using System.Collections.Generic;
using Unity.Burst;
using Unity.Collections;
using Unity.Entities;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;

public class PlanarReflectionScript : MonoBehaviour
{
    public enum ResolutionMultipliers
    {
        Full,
        Half,
        Third,
        Quarter
    }

    private static EntityArchetype _cameraArchetype;
    private bool _currentHdRsetting;
    private int _currentRenderTextureint;
    private World _defaultWorld;
    private Camera _entityAttachedCam;
    private EntityManager _entityManager;
    private long _fpsCounter;
    private bool _processingRenderCamera;
    private RenderTexture _reflTexture;
    private RenderTexture _reflTextureCopy;
    private readonly RenderTexture[] _renderTexturearray = new RenderTexture[1];
    private bool _skipFrame;
    private readonly Camera[] cameras = new Camera[1];
    public PlanarReflectionSettings planarLayerSettings = new PlanarReflectionSettings();
    private Camera TargetCamera;
    private ScriptableRenderContext TargetContext;

    [HideInInspector] public Camera thisCamera;

    private float GetScaleValue()
    {
        switch (planarLayerSettings.resolutionMultiplier)
        {
            case ResolutionMultipliers.Full:
                return 1f;
            case ResolutionMultipliers.Half:
                return 0.5f;
            case ResolutionMultipliers.Third:
                return 0.33f;
            case ResolutionMultipliers.Quarter:
                return 0.25f;
            default:
                return 0.5f;
        }
    }

    private void OnDisable()
    {
        Cleanup();
    }

    private void OnDestroy()
    {
        Cleanup();
    }

    private void Cleanup()
    {
        RenderPipelineManager.beginCameraRendering -= ExecutePlanarReflections;

        if (!_reflTexture) return;
        RenderTexture.ReleaseTemporary(_reflTexture);

        _reflTexture = null;
        if (cameraSpacePlaneResult.IsCreated)
            cameraSpacePlaneResult.Dispose();
        if (resultMatrix.IsCreated)
            resultMatrix.Dispose();
    }

    private void Update()
    {
        _fpsCounter++;
    }

    private void Start()
    {
        _defaultWorld = World.DefaultGameObjectInjectionWorld;
        _entityManager = _defaultWorld.EntityManager;
        thisCamera = GetComponent<Camera>();
        RenderPipelineManager.beginCameraRendering += ExecutePlanarReflections;
    }

    private void ExecutePlanarReflections(ScriptableRenderContext arg1, Camera arg2)
    {
        if (arg2 == Camera.main)
        {
            if (planarLayerSettings.recursiveReflection)
                return;
            ExecuteRenderSequence(null, arg1);
        }
    }

    public IList<Camera> ExecuteRenderSequence(Camera sentCamera = null,
        ScriptableRenderContext src = new ScriptableRenderContext(), bool inverted = true, bool enableRender = true)
    {
        if (this != null)
        {
            var targetCamera = RenderTargetCamera(sentCamera, inverted, enableRender, src);
            cameras[0] = targetCamera;
        }
        else
        {
            cameras[0] = null;
        }

        return cameras;
    }

    private NativeArray<float4> cameraSpacePlaneResult;
    private NativeArray<Matrix4x4> resultMatrix;
    private Camera RenderTargetCamera(Camera sentCamera, bool invert = true, bool renderCam = true,
        ScriptableRenderContext src = new ScriptableRenderContext())
    {
        var cameraToUse = sentCamera;
        if (cameraToUse == null && gameObject != null)
            cameraToUse = thisCamera;
        if (cameraToUse.cameraType == CameraType.Reflection)
            return null;
        _skipFrame = _fpsCounter % planarLayerSettings.frameSkip != 0;
        if (_skipFrame) return null;
        if (_processingRenderCamera)
            return null;
        _processingRenderCamera = true;
        var fogcache = RenderSettings.fog;
        RenderSettings.fog = false;
        CreateMirrorObjects(cameraToUse, out var reflectionCamera);
        UpdateCameraModes(cameraToUse, reflectionCamera);
        reflectionCamera.cullingMask = planarLayerSettings.reflectLayers;
        var normal = planarLayerSettings.direction;
        var d = -planarLayerSettings.clipPlaneOffset;
        var reflectionPlane = new float4(normal.x, normal.y, normal.z, d);
        var reflection = Matrix4x4.identity;
        resultMatrix = new NativeArray<Matrix4x4>(1, Allocator.TempJob);
        var calculateReflectionMatrix = new CalculateReflectionMatrixJob
        {
            reflectionMat = reflection,
            plane = reflectionPlane,
            ResultMatrix = resultMatrix
        };
        var handle = calculateReflectionMatrix.Schedule();


        cameraSpacePlaneResult = new NativeArray<float4>(1, Allocator.TempJob);
        var cameraSpacePlaneJob = new CameraSpacePlaneJob();
        cameraSpacePlaneJob.Normal = normal;
        cameraSpacePlaneJob.ResultMatrix = resultMatrix;
        cameraSpacePlaneJob.SideSign = invert ? 1.0f : -1.0f;
        cameraSpacePlaneJob.OffsetPos = normal * planarLayerSettings.clipPlaneOffset;
        cameraSpacePlaneJob.WorldToCameraMatrix = cameraToUse.worldToCameraMatrix;
        cameraSpacePlaneJob.CameraSpacePlaneResult = cameraSpacePlaneResult;
        var cameraSpaceHandle = cameraSpacePlaneJob.Schedule(handle);

        var projectionMatrix = cameraToUse.projectionMatrix;
        var matrixtemp = new NativeArray<Matrix4x4>(1, Allocator.TempJob);
        var makeProjectionMatrixObliqueJob = new MakeProjectionMatrixObliqueJob
        {
            Matrix = projectionMatrix, Matrixtemp = matrixtemp, cameraSpacePlaneResult = cameraSpacePlaneResult
        };

        var makeProjectionMatrixObliqueHandle = makeProjectionMatrixObliqueJob.Schedule(cameraSpaceHandle);
        makeProjectionMatrixObliqueHandle.Complete();
        var worldToCameraMatrix = cameraToUse.worldToCameraMatrix * resultMatrix[0];
        reflectionCamera.transform.position = cameraToUse.transform.position;
        reflectionCamera.worldToCameraMatrix = worldToCameraMatrix;
        cameraSpacePlaneJob.CameraSpacePlaneResult = cameraSpacePlaneResult;

        projectionMatrix = matrixtemp[0];
        matrixtemp.Dispose();
        cameraSpacePlaneResult.Dispose();
        resultMatrix.Dispose();


        reflectionCamera.projectionMatrix = projectionMatrix;
        reflectionCamera.transform.rotation = cameraToUse.transform.rotation;
        var oldInvertCulling = GL.invertCulling;
        GL.invertCulling = invert;
        reflectionCamera.targetTexture = _reflTexture;
        if (renderCam) UniversalRenderPipeline.RenderSingleCamera(src, reflectionCamera);
        GL.invertCulling = oldInvertCulling;
        if (renderCam) UpdateMaterialProperties(cameraToUse);
        RenderSettings.fog = fogcache;
        _processingRenderCamera = false;
        return reflectionCamera;
    }

    public void UpdateMaterialProperties(Camera sentCamera)
    {
        Shader.SetGlobalTexture(planarLayerSettings.shaderPropertyName, _reflTexture);
    }

    public RenderTexture[] CopyTextures()
    {
        if (!_reflTextureCopy && _reflTexture) _reflTextureCopy = new RenderTexture(_reflTexture);
        if (_reflTexture != null) Graphics.Blit(_reflTexture, _reflTextureCopy);

        _renderTexturearray[0] = _reflTextureCopy;
        return _renderTexturearray;
    }

    public void PasteTextures(IList<RenderTexture> textures)
    {
        if (textures != null && textures.Count >= 4) _reflTexture = textures[0];
    }

    private static float SignCheck(float a)
    {
        if (a > 0.0f) return 1.0f;
        if (a < 0.0f) return -1.0f;
        return 0.0f;
    }

    private void UpdateCameraModes(Camera src, Camera dest)
    {
        if (dest == null)
        {
            return;
        }

        if (planarLayerSettings.addBlackColour)
        {
            dest.clearFlags = CameraClearFlags.Color;
            dest.backgroundColor = new Color(0, 0, 0, 1);
        }
        else
        {
            dest.clearFlags = src.clearFlags;
            dest.backgroundColor = src.backgroundColor;
        }

        if (dest.gameObject.TryGetComponent(out UniversalAdditionalCameraData camData))
            camData.renderShadows = planarLayerSettings.shadows;
        dest.nearClipPlane = src.nearClipPlane;
        dest.farClipPlane = src.farClipPlane;
        dest.orthographic = src.orthographic;
        dest.fieldOfView = src.fieldOfView;
        dest.aspect = src.aspect;
        dest.orthographicSize = src.orthographicSize;
        dest.allowHDR = planarLayerSettings.enableHdr;
        dest.allowMSAA = planarLayerSettings.enableMSAA;
        dest.useOcclusionCulling = planarLayerSettings.occlusion;
    }

    public int2 ReflectionResolution(Camera cam, float scale)
    {
        var x = (int) (cam.pixelWidth * scale * GetScaleValue());
        var y = (int) (cam.pixelHeight * scale * GetScaleValue());
        return new int2(x, y);
    }

    private void CreateMirrorObjects(Camera currentCamera, out Camera reflectionCamera)
    {
        var depth = 24;
        var textureSize = ReflectionResolution(currentCamera, UniversalRenderPipeline.asset.renderScale);
        var textureFormatHDR = RenderTextureFormat.DefaultHDR;
        var textureFormat = RenderTextureFormat.Default;
        if (!_reflTexture ||
            planarLayerSettings.enableHdr != _currentHdRsetting
            || _currentRenderTextureint != textureSize[0])
        {
            if (_reflTexture)
                RenderTexture.ReleaseTemporary(_reflTexture);
            if (planarLayerSettings.enableHdr)
                _reflTexture = RenderTexture.GetTemporary(textureSize[0], textureSize[1], depth, textureFormatHDR);
            else
                _reflTexture = RenderTexture.GetTemporary(textureSize[0], textureSize[1], depth, textureFormat);
            if (QualitySettings.antiAliasing > 0)
                _reflTexture.antiAliasing = QualitySettings.antiAliasing;
            _currentRenderTextureint = textureSize[0];
            _currentHdRsetting = planarLayerSettings.enableHdr;
        }

        if (_entityAttachedCam != null)
        {
            reflectionCamera = _entityAttachedCam;
        }
        else
        {
            var query = _entityManager.CreateEntityQuery(typeof(CamObjectStruct))
                .ToEntityArray(Allocator.TempJob);
            if (query.Length == 0)
            {
                _cameraArchetype =
                    _entityManager.CreateArchetype(typeof(CamObjectStruct));
                var camEntity = _entityManager.CreateEntity(_cameraArchetype);
                var go = new GameObject();
                go.AddComponent<Camera>();
                var cameraData =
                    go.AddComponent(typeof(UniversalAdditionalCameraData)) as UniversalAdditionalCameraData;
                if (cameraData != null)
                {
                    cameraData.requiresColorOption = CameraOverrideOption.Off;
                    cameraData.requiresDepthOption = CameraOverrideOption.Off;
                    cameraData.SetRenderer(0);
                }

                var reflectionCam = go.GetComponent<Camera>();
                reflectionCam.depth = -10;
                reflectionCam.enabled = false;
                go.hideFlags = HideFlags.HideAndDontSave;
                _entityManager.SetComponentData(camEntity,
                    new CamObjectStruct
                    {
                        Cam = go.GetComponent<Camera>(),
                        Uacd = go.GetComponent<UniversalAdditionalCameraData>()
                    });
                var tempcam = _entityManager
                    .GetComponentData<CamObjectStruct>(camEntity).Cam;
                reflectionCamera = tempcam;
                reflectionCamera.enabled = false;
                _entityAttachedCam = tempcam;
            }
            else
            {
                var tempcam = _entityManager
                    .GetComponentData<CamObjectStruct>(query[0]).Cam;
                reflectionCamera = tempcam;
                _entityAttachedCam = tempcam;
            }

            query.Dispose();
        }
    }

    [Serializable]
    public class PlanarReflectionSettings
    {
        public bool addBlackColour;
        public float clipPlaneOffset = 0.07f;
        public float3 direction;
        public bool enableHdr;
        public bool enableLights;
        public bool enableMSAA;
        public int frameSkip = 1;
        public bool occlusion;
        public int recursiveGroup = 1;
        public bool recursiveReflection;
        public LayerMask reflectLayers = -1;
        public ResolutionMultipliers resolutionMultiplier;
        public string shaderPropertyName;
        public bool shadows;
    }

    [BurstCompile(CompileSynchronously = false)]
    private struct MakeProjectionMatrixObliqueJob : IJob
    {
        public NativeArray<float4> cameraSpacePlaneResult;
        public float4 ClipPlane;
        public NativeArray<Matrix4x4> Matrixtemp;
        public Matrix4x4 Matrix;

        public void Execute()
        {
            ClipPlane = cameraSpacePlaneResult[0];
            float4 q;
            q.x = (SignCheck(ClipPlane.x) + Matrix[8]) / Matrix[0];
            q.y = (SignCheck(ClipPlane.y) + Matrix[9]) / Matrix[5];
            q.z = -1.0F;
            q.w = (1.05F + Matrix[10]) / Matrix[14];
            var c = ClipPlane * (2.0F / math.dot(ClipPlane, q));
            Matrix[2] = c.x;
            Matrix[6] = c.y;
            Matrix[10] = c.z + 1.0F;
            Matrix[14] = c.w;
            Matrixtemp[0] = Matrix;
        }
    }

    [BurstCompile(CompileSynchronously = false)]
    private struct CameraSpacePlaneJob : IJob
    {
        public NativeArray<Matrix4x4> ResultMatrix;
        public float3 OffsetPos;
        public float3 Normal;
        public Matrix4x4 WorldToCameraMatrix;
        public float SideSign;
        public NativeArray<float4> CameraSpacePlaneResult;

        public void Execute()
        {
            WorldToCameraMatrix = WorldToCameraMatrix * ResultMatrix[0];
            float3 cameraPosition = WorldToCameraMatrix.MultiplyPoint(OffsetPos);
            float3 cameraNormal = WorldToCameraMatrix.MultiplyVector(Normal).normalized * SideSign;
            CameraSpacePlaneResult[0] = new float4(cameraNormal.x, cameraNormal.y, cameraNormal.z,
                -math.dot(cameraPosition, cameraNormal));
        }
    }

    [BurstCompile(CompileSynchronously = false)]
    private struct CalculateReflectionMatrixJob : IJob
    {
        public float4 plane;
        public Matrix4x4 reflectionMat;
        public NativeArray<Matrix4x4> ResultMatrix;

        public void Execute()
        {
            reflectionMat.m00 = 1F - 2F * plane[0] * plane[0];
            reflectionMat.m01 = -2F * plane[0] * plane[1];
            reflectionMat.m02 = -2F * plane[0] * plane[2];
            reflectionMat.m03 = -2F * plane[3] * plane[0];
            reflectionMat.m10 = -2F * plane[1] * plane[0];
            reflectionMat.m11 = 1F - 2F * plane[1] * plane[1];
            reflectionMat.m12 = -2F * plane[1] * plane[2];
            reflectionMat.m13 = -2F * plane[3] * plane[1];
            reflectionMat.m20 = -2F * plane[2] * plane[0];
            reflectionMat.m21 = -2F * plane[2] * plane[1];
            reflectionMat.m22 = 1F - 2F * plane[2] * plane[2];
            reflectionMat.m23 = -2F * plane[3] * plane[2];
            reflectionMat.m30 = 0F;
            reflectionMat.m31 = 0F;
            reflectionMat.m32 = 0F;
            reflectionMat.m33 = 1F;
            ResultMatrix[0] = reflectionMat;
        }
    }
}

[Serializable]
public class CamObjectStruct : IComponentData
{
    public UniversalAdditionalCameraData Uacd { get; set; }
    public Camera Cam { get; set; }
}