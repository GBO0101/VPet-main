using System;

namespace VPet_Simulator.Windows.AiAgent;

internal static class AiAgentEnvironment
{
    public const string AiProvider = "VPET_AI_PROVIDER";
    public const string OllamaAutoStart = "VPET_OLLAMA_AUTO_START";
    public const string OllamaExePath = "VPET_OLLAMA_EXE";
    public const string OllamaUrl = "VPET_OLLAMA_URL";
    public const string OllamaModel = "VPET_OLLAMA_MODEL";
    public const string OpenAiApiKey = "OPENAI_API_KEY";
    public const string OpenAiModel = "VPET_OPENAI_MODEL";
    public const string RemoteApiBaseUrl = "VPET_REMOTE_API_BASE_URL";
    public const string RemoteApiKey = "VPET_REMOTE_API_KEY";
    public const string RemoteApiModel = "VPET_REMOTE_API_MODEL";
    public const string GoogleClientId = "VPET_GOOGLE_CLIENT_ID";
    public const string GoogleClientSecret = "VPET_GOOGLE_CLIENT_SECRET";
    public const string GoogleRefreshToken = "VPET_GOOGLE_REFRESH_TOKEN";
    public const string CwaApiKey = "VPET_CWA_API_KEY";
    public const string DefaultLocation = "VPET_DEFAULT_LOCATION";

    public static string Provider
    {
        get
        {
            var provider = Get(AiProvider);
            return string.IsNullOrWhiteSpace(provider) ? "ollama" : provider;
        }
    }

    public static bool IsOllamaAutoStartEnabled
    {
        get
        {
            var value = Get(OllamaAutoStart);
            return string.IsNullOrWhiteSpace(value) || value.Equals("true", StringComparison.OrdinalIgnoreCase);
        }
    }

    public static string Get(string name)
    {
        return Environment.GetEnvironmentVariable(name)
            ?? Environment.GetEnvironmentVariable(name, EnvironmentVariableTarget.User)
            ?? "";
    }

    public static void SetUser(string name, string value)
    {
        Environment.SetEnvironmentVariable(name, string.IsNullOrWhiteSpace(value) ? null : value, EnvironmentVariableTarget.User);
    }

    public static string GetSelectedModel()
    {
        return Provider.Equals("ollama", StringComparison.OrdinalIgnoreCase)
            ? Get(OllamaModel)
            : Get(RemoteApiModel);
    }

    public static string GetRemoteApiKey()
    {
        var key = Get(RemoteApiKey);
        return string.IsNullOrWhiteSpace(key) ? Get(OpenAiApiKey) : key;
    }

    public static string GetRemoteApiModel()
    {
        var model = Get(RemoteApiModel);
        return string.IsNullOrWhiteSpace(model) ? Get(OpenAiModel) : model;
    }
}
