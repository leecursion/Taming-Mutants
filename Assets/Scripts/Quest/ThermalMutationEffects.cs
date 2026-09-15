using System.Collections.Generic;
using UnityEngine;

/// <summary>Heating-baseline ghosts and a persistent mutation marker; stabilization does not erase mutation.</summary>
public class ThermalMutationEffects : MonoBehaviour
{
    private LineRenderer _mutation, _wave;
    private readonly List<LineRenderer> _ghosts = new List<LineRenderer>();
    private ThermalStabilityController _thermal;
    private float _settled;
    private float _radius;

    public void Initialize(ThermalStabilityController thermal, Vector3 mutation, IList<Vector3> baseline)
    {
        _thermal = thermal;
        transform.localPosition = mutation;
        _radius = Mathf.Max(.08f, thermal.wobbleRadius * .25f);
        _mutation = MutationExperimentEffects.Line(transform, "Y220C", new Color(.8f, .3f, 1f), .006f);
        _wave = MutationExperimentEffects.Line(transform, "ThermalWave", Color.magenta, .004f);
        foreach (Vector3 point in baseline)
        {
            var ghost = MutationExperimentEffects.Line(transform, "HeatingBaseline", new Color(.6f, .85f, 1f, .25f), .0025f);
            ghost.transform.localPosition = point - mutation;
            MutationExperimentEffects.Ring(ghost, .025f);
            _ghosts.Add(ghost);
        }
    }

    private void Update()
    {
        if (_thermal == null) { Destroy(gameObject); return; }
        _settled = Mathf.MoveTowards(_settled, _thermal.IsStabilized ? 1f : 0f, Time.deltaTime / 1.5f);
        float heat = _thermal.Normalized01 * Mathf.Lerp(1f, .12f, _settled);
        Camera cam = _thermal.targetCamera;
        if (cam != null)
        {
            _mutation.transform.rotation = _wave.transform.rotation = cam.transform.rotation;
            foreach (var ghost in _ghosts) ghost.transform.rotation = cam.transform.rotation;
        }
        MutationExperimentEffects.Ring(_mutation, _radius * (1f + .045f * Mathf.Sin(Time.time * 2f)));
        Color violet = new Color(.8f, .3f, 1f, .45f + .15f * Mathf.Sin(Time.time * 2f));
        _mutation.startColor = _mutation.endColor = violet;
        float phase = Mathf.Repeat(Time.time * Mathf.Lerp(.18f, .5f, heat), 1f);
        MutationExperimentEffects.Ring(_wave, _radius * (1f + phase * .7f), 1f, heat * .08f);
        Color waveColor = Color.Lerp(new Color(1f, .35f, .6f), new Color(.25f, 1f, .8f), _settled);
        waveColor.a = (1f - phase) * (.12f + heat * .45f);
        _wave.startColor = _wave.endColor = waveColor;
    }
}
