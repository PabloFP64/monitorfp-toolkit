using UnityEngine;

public class InterestingGameObject : MonoBehaviour
{
    [SerializeField] private string displayNameOverride = string.Empty;
    [SerializeField] private Transform observationTarget;

    public string DisplayName
    {
        get
        {
            if (!string.IsNullOrWhiteSpace(displayNameOverride))
            {
                return displayNameOverride.Trim();
            }

            return gameObject.name;
        }
    }

    public Transform ObservationTarget => observationTarget != null ? observationTarget : transform;

    private void OnEnable()
    {
        ObservationTracker.RegisterInterestingObject(this);
    }

    private void OnDisable()
    {
        ObservationTracker.UnregisterInterestingObject(this);
    }
}
