# Contributing to Kube-Mind Brain Plugins

We welcome contributions to extend the capabilities of the Kube-Mind Brain by adding new [Semantic Kernel Plugins](https://learn.microsoft.com/en-us/semantic-kernel/concepts/plugins/)

## Plugin Creation Workflow

Follow these steps to create a new plugin for the Kube-Mind Brain:

1.  **Define your Plugin Class:**

    *   Create a new C# class in the `brain/src/KubeMind.Brain.Application/Plugins` directory.
    *   The class should have a descriptive name that reflects its functionality (e.g., `SlackPlugin`, `JiraPlugin`).
    *   Use [Primary Constructors](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-12#primary-constructors) for dependency injection (e.g., `ILogger`, `IHttpClientFactory`, your custom services defined in `KubeMind.Brain.Application.Services`).

    ```csharp
    using System.ComponentModel;
    using Microsoft.SemanticKernel;
    using Microsoft.Extensions.Logging;

    namespace KubeMind.Brain.Application.Plugins;

    public class MyNewPlugin(ILogger<MyNewPlugin> logger)
    {
        [KernelFunction]
        [Description("A short, descriptive summary of what this function does.")]
        public async Task<string> MyPluginFunction(
            [Description("Description of the first parameter.")] string param1,
            [Description("Description of the second parameter.")] int param2)
        {
            logger.LogInformation("MyPluginFunction called with param1: {Param1}, param2: {Param2}", param1, param2);

            // Implement your plugin logic here
            await Task.Delay(100); // Simulate async work

            return $"Result of MyPluginFunction for {param1} and {param2}";
        }
    }
    ```

2.  **Add `KernelFunction` and `Description` Attributes:**

    *   Mark each public method that you want to expose to the AI as a callable function with the `[KernelFunction]` attribute.
    *   Provide a clear and concise `[Description]` attribute for both the function and its parameters. This description is what the AI will use to understand when and how to call your function.

3.  **Register your Plugin:**

    *   Open `brain/src/KubeMind.Brain.Api/Program.cs`.
    *   Locate the section where plugins are registered (e.g., `kernelBuilder.Plugins.AddFromType<T>()`).
    *   Add a new line to register your plugin:

    ```csharp
    builder.Services.AddSingleton<KubeMind.Brain.Application.Plugins.MyNewPlugin>();
    kernelBuilder.Plugins.AddFromType<KubeMind.Brain.Application.Plugins.MyNewPlugin>();
    ```

4.  **Add Unit Tests:**

    *   Create a corresponding test file (e.g., `MyNewPluginTests.cs`) in `brain/tests/KubeMind.Brain.Tests`.
    *   Use `xUnit` and `Moq` (or NSubstitute) to write comprehensive unit tests for your plugin's logic.
    *   Follow the **Arrange-Act-Assert** pattern and use descriptive test method names.

    ```csharp
    using KubeMind.Brain.Application.Plugins;
    using Microsoft.Extensions.Logging;
    using Microsoft.SemanticKernel;
    using Moq;
    using Xunit;

    namespace KubeMind.Brain.Tests;

    public class MyNewPluginTests
    {
        [Fact]
        public async Task MyPluginFunction_ReturnsExpectedResult()
        {
            // Arrange
            var mockLogger = new Mock<ILogger<MyNewPlugin>>();
            var plugin = new MyNewPlugin(mockLogger.Object);
            var param1 = "test";
            var param2 = 123;
            var expected = $"Result of MyPluginFunction for {param1} and {param2}";

            // Act
            var result = await plugin.MyPluginFunction(param1, param2);

            // Assert
            Assert.Equal(expected, result);
            mockLogger.Verify(x => x.Log(LogLevel.Information, It.IsAny<EventId>(), It.IsAny<It.IsAnyType>(), It.IsAny<Exception>(), It.IsAny<Func<It.IsAnyType, Exception?, string>>()), Times.Once);
        }
    }
    ```

5.  **Build and Test:**

    *   Ensure your changes compile successfully:
        ```bash
        dotnet build
        ```
    *   Run your tests:
        ```bash
        dotnet test
        ```

## Code Style and Best Practices

*   **No Comments in Code:** Prioritize self-documenting code with clear variable and method names. Only use `///` XML documentation for public APIs or complex algorithms.
*   **Asynchronous Operations:** Use `async`/`await` for all I/O-bound operations. Favor `Task` over `async void`.
*   **Dependency Injection:** Use primary constructors for injecting dependencies.
*   **Logging:** Use `ILogger<T>` for structured logging.
*   **Immutability:** Favor `init`-only setters and `record struct` where appropriate.

This guide will be expanded with more details on specific architectural patterns and testing strategies soon.
