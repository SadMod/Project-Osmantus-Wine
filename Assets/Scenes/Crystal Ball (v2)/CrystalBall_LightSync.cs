using JetBrains.Annotations;
using System;
using UnityEngine;

[ExecuteInEditMode]
public class CrystalBall_LightSync : MonoBehaviour
{
    [SerializeField] private MeshRenderer Source;
    [SerializeField, NotNull] private string SourcePropertyForSampling_color = "_Fresnel_color";
    private Color SourcePropertyForSampling_color_value;
    [SerializeField, NotNull] private string SourcePropertyForSampling_intensity = "_Fresnel_width";
    private float SourcePropertyForSampling_intensity_value;
    [SerializeField] private float proportionFromSourceToTarget = 100;
    [SerializeField] private Light Target;
    private readonly Backup _backup = new Backup();

    [Serializable]
    protected class Backup
    {
        public MeshRenderer Source;
        public string SourcePropertyForSampling_color, SourcePropertyForSampling_intensity;
        public Color SourcePropertyForSampling_color_value;
        public float SourcePropertyForSampling_intensity_value;
        public Light Target;
    }

    private void OnEnable()
    {
        UpdateAll();
    }

    public void UpdateAll()
    {
        SourcePropertyForSampling_color_value = Source.material.GetColor(SourcePropertyForSampling_color);
        SourcePropertyForSampling_intensity_value = Source.material.GetFloat(SourcePropertyForSampling_intensity) / proportionFromSourceToTarget;

        _backup.Source = Source;
        _backup.SourcePropertyForSampling_color = SourcePropertyForSampling_color;
        _backup.SourcePropertyForSampling_color_value = SourcePropertyForSampling_color_value;
        _backup.SourcePropertyForSampling_intensity = SourcePropertyForSampling_intensity;
        _backup.SourcePropertyForSampling_intensity_value = SourcePropertyForSampling_intensity_value;
        _backup.Target = Target;

        //if (!(GetPropertyValue(Source.material, SourcePropertyForSampling) is Color value))
        //    throw new NullReferenceException();

        Target.color = SourcePropertyForSampling_color_value;
        Target.intensity = SourcePropertyForSampling_intensity_value;
    }


    private void Update()
    {
        if (_backup.Source == Source && 
            _backup.SourcePropertyForSampling_color == SourcePropertyForSampling_color &&
            _backup.SourcePropertyForSampling_intensity == SourcePropertyForSampling_intensity &&
            _backup.Target == Target &&
            _backup.SourcePropertyForSampling_color_value == Source.material.GetColor(SourcePropertyForSampling_color) &&
            _backup.SourcePropertyForSampling_intensity_value == Source.material.GetFloat(SourcePropertyForSampling_intensity) / proportionFromSourceToTarget
            ) return;
        UpdateAll();
    }


    //public static object GetPropertyValue(object source, string property)
    //{
    //    return source.GetType().GetProperties()
    //        .Single(pi => pi.Name == property)
    //        .GetValue(source, null);
    //}
}