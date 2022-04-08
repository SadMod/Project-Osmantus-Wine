using UnityEngine;
using UnityEngine.Rendering.Universal;

public class HolomapRenderCameraController : MonoBehaviour
{
    public Camera camera;
    private UniversalAdditionalCameraData _cameraData;
    private UniversalAdditionalCameraData cameraData => _cameraData is null || camera.GetUniversalAdditionalCameraData() != _cameraData ? _cameraData = camera.GetUniversalAdditionalCameraData() : _cameraData;
    public RenderTexture OutputTexture;

    /*void OnEnable()
    {
        cameraData.cameraOutput = ;
        camera.targetTexture = OutputTexture;
    }
	*/
}
