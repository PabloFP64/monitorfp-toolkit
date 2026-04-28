using System;

[Serializable]
public struct InterestingObjectObservationStats
{
    public string name;
    public float firstSeenAtSeconds;
    public float totalObservedSeconds;
    public float[] observationSegmentsSeconds;
    public int centerObservationCount;
    public float centerObservedSeconds;
    public float peripheralObservedSeconds;
    public float centerTimePercent;
    public bool currentlyObserved;
}
