using System;

namespace VPet_Simulator.Windows.AiAgent;

internal static class FeatureManager
{
    public static bool IsVoiceInputEnabled => IsEnabled(AiAgentEnvironment.FeatureVoiceInput, true);
    public static bool IsVoiceOutputEnabled => IsEnabled(AiAgentEnvironment.FeatureVoiceOutput, true);
    public static bool IsScreenAwareEnabled => IsEnabled(AiAgentEnvironment.FeatureScreenAware, true);
    public static bool IsWorkflowEnabled => IsEnabled(AiAgentEnvironment.FeatureWorkflow, true);

    private static bool IsEnabled(string envName, bool defaultValue)
    {
        var value = AiAgentEnvironment.Get(envName);
        if (string.IsNullOrWhiteSpace(value))
            return defaultValue;
        return value.Equals("true", StringComparison.OrdinalIgnoreCase);
    }

    public static void SetVoiceInputEnabled(bool enabled)
        => AiAgentEnvironment.SetUser(AiAgentEnvironment.FeatureVoiceInput, enabled ? "true" : "false");

    public static void SetVoiceOutputEnabled(bool enabled)
        => AiAgentEnvironment.SetUser(AiAgentEnvironment.FeatureVoiceOutput, enabled ? "true" : "false");

    public static void SetScreenAwareEnabled(bool enabled)
        => AiAgentEnvironment.SetUser(AiAgentEnvironment.FeatureScreenAware, enabled ? "true" : "false");

    public static void SetWorkflowEnabled(bool enabled)
        => AiAgentEnvironment.SetUser(AiAgentEnvironment.FeatureWorkflow, enabled ? "true" : "false");
}
