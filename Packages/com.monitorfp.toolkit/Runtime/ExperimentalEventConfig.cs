using System.Collections.Generic;
using UnityEngine;

public class ExperimentalEventConfig : MonoBehaviour
{
    [Tooltip("List of event labels that will appear as buttons in the web dashboard")]
    public List<string> eventLabels = new List<string>() { "Start Trial", "Important Event", "End Trial" };
}
