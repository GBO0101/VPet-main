using System.Collections.Generic;
using System.Linq;

namespace VPet_Simulator.Windows.AiAgent;

internal sealed class WorkflowStore
{
    private const string FileName = "AiAgentWorkflows.json";

    public List<WorkflowDefinition> Load()
    {
        return AiAgentJsonStore.LoadList<WorkflowDefinition>(FileName)
            .Where(w => !string.IsNullOrWhiteSpace(w.Name))
            .ToList();
    }

    public void Save(List<WorkflowDefinition> workflows)
    {
        AiAgentJsonStore.SaveList(FileName, workflows
            .Where(w => !string.IsNullOrWhiteSpace(w.Name))
            .ToList());
    }
}
