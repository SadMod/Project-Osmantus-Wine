using UnityEngine;

public class NecraliskBallRotator : MonoBehaviour
{
    public Vector3 SpeedAndDirection = new Vector3(1,1,1);
    private float time = 0;
    [SerializeField] private float SinMultiplier = 1;
    void Update()
    {
        transform.Rotate(GetSin_edVector(SpeedAndDirection), Space.Self);
        time += Time.deltaTime;
    }

    private Vector3 GetSin_edVector(Vector3 source) => new Vector3(SinIt(source.x), SinIt(source.y), SinIt(source.z));

    private float SinIt(float source) => Mathf.Sin(source * time * SinMultiplier);
}
