using Microsoft.SemanticKernel;

namespace KubeMind.Brain.Api.Extensions
{
    public static class ServiceCollectionExtensions
    {
        public static IServiceCollection AddAiService(this IServiceCollection services, IConfiguration configuration)
        {
            var aiConfig = configuration.GetSection("AIService");
            var serviceType = aiConfig["Type"];

            if (string.IsNullOrWhiteSpace(serviceType))
            {
                throw new InvalidOperationException("AIService:Type is not configured in appsettings.");
            }

            var modelId = aiConfig["ModelId"];
            var apiKey = aiConfig["ApiKey"];

            if (string.IsNullOrWhiteSpace(modelId)) throw new InvalidOperationException("AIService:ModelId is not configured.");
            if (string.IsNullOrWhiteSpace(apiKey)) throw new InvalidOperationException("AIService:ApiKey is not configured.");

            switch (serviceType)
            {
                case "OpenAI":
                    var orgId = aiConfig["OrgId"];
                    services.AddOpenAIChatCompletion(modelId, apiKey, orgId);
                    break;

                case "AzureOpenAI":
                    var endpoint = aiConfig["Endpoint"];
                    if (string.IsNullOrWhiteSpace(endpoint)) throw new InvalidOperationException("AIService:Endpoint is not configured for AzureOpenAI.");
                    services.AddAzureOpenAIChatCompletion(modelId, endpoint, apiKey);
                    break;
                
                case "Gemini":
                    services.AddGoogleAIGeminiChatCompletion(modelId, apiKey);
                    break;

                case "Google":
                    services.AddGoogleAIGeminiChatCompletion(modelId, apiKey);
                    break;

                default:
                    throw new InvalidOperationException($"Unsupported AIService:Type '{serviceType}'.");
            }

            return services;
        }
    }
}
