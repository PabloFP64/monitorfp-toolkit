using UnityEngine;

public enum ObservationTrackingStartMode
{
    OnSceneStart,
    Manual
}

public class ObservationTrackingSettings : MonoBehaviour
{
    [Header("Inicio")]
    [SerializeField] private ObservationTrackingStartMode startMode = ObservationTrackingStartMode.OnSceneStart;

    [Header("Centro de vision")]
    [SerializeField, Range(0.01f, 0.45f)] private float centerCircleRadius = 0.12f;

    [Header("Deteccion")]
    [SerializeField] private Camera observationCamera;
    [SerializeField] private bool requireLineOfSight = true;
    [SerializeField] private float maxObservationDistance = 100f;
    [SerializeField] private float rediscoveryInterval = 1f;

    public ObservationTrackingStartMode StartMode => startMode;
    public float CenterCircleRadius => Mathf.Clamp(centerCircleRadius, 0.01f, 0.45f);
    public Camera ObservationCamera => observationCamera;
    public bool RequireLineOfSight => requireLineOfSight;
    public float MaxObservationDistance => Mathf.Max(0.5f, maxObservationDistance);
    public float RediscoveryInterval => Mathf.Max(0.1f, rediscoveryInterval);
}
