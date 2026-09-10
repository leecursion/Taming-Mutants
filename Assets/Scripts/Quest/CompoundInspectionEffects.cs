using UnityEngine;

/// <summary>Neutral DNA-shaped hold progress; never reveals the candidate's outcome.</summary>
public class CompoundInspectionEffects : MonoBehaviour
{
    private LineRenderer _a, _b;
    private LineRenderer[] _rungs;
    private float _size;

    public void SetProgress(float progress, float size)
    {
        if (_a == null)
        {
            _size = size;
            _a = MutationExperimentEffects.Line(transform, "DNA_A", Color.cyan, size * .013f);
            _b = MutationExperimentEffects.Line(transform, "DNA_B", new Color(.7f, .4f, 1f), size * .013f);
            _rungs = new LineRenderer[9];
            for (int i = 0; i < _rungs.Length; i++)
                _rungs[i] = MutationExperimentEffects.Line(transform, "BasePair", new Color(.5f, .8f, 1f, .6f), size * .007f);
        }
        _a.gameObject.SetActive(progress > 0f);
        _b.gameObject.SetActive(progress > 0f);
        _a.positionCount = _b.positionCount = 49;
        for (int i = 0; i < 49; i++)
        {
            float p = i / 48f * progress;
            _a.SetPosition(i, Point(p, 0f));
            _b.SetPosition(i, Point(p, Mathf.PI));
        }
        for (int i = 0; i < _rungs.Length; i++)
        {
            float p = (i + 1) / 10f;
            _rungs[i].gameObject.SetActive(progress >= p);
            _rungs[i].positionCount = 2;
            _rungs[i].SetPosition(0, Point(p, 0f));
            _rungs[i].SetPosition(1, Point(p, Mathf.PI));
        }
    }

    private Vector3 Point(float p, float phase)
    {
        float angle = p * Mathf.PI * 4f + phase;
        return new Vector3(Mathf.Sin(angle) * _size * .3f, (p - .5f) * _size,
            -_size * .6f + Mathf.Cos(angle) * _size * .05f);
    }
}
