using System.ComponentModel.DataAnnotations;

namespace AcademicCollectorDemo.Modules.AcademicPerformance.FacultyAssistant;

public sealed class FacultyAssistantOptions
{
    public bool WorkerEnabled { get; set; } = true;
    [Range(1, 60)] public int PollSeconds { get; set; } = 5;
    [Range(10, 1800)] public int RequestTimeoutSeconds { get; set; } = 1800;
    [Required, StringLength(100)] public string RetrievalPolicyVersion { get; set; } = "faculty-assistant-retrieval-v1";
}
