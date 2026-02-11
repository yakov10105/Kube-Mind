# Kube-Mind Local Development Setup

This guide provides instructions for setting up your local development environment to run the Kube-Mind "Brain" service. This includes configuring a Google Cloud Platform (GCP) project for AI services, running a local Redis container, and setting up a GitHub App for GitOps functionality.

## 1. Google Cloud Platform (GCP) Setup for Gemini API

Kube-Mind uses the Gemini API for its AI-driven diagnostics. Follow these steps to set up a GCP project and obtain an API key.

### Prerequisites

*   A Google account with billing enabled.

### Steps

1.  **Create a new GCP Project:**
    *   Go to the [GCP Console](https://console.cloud.google.com/).
    *   In the top left corner, click the project dropdown and select **New Project**.
    *   Give your project a name (e.g., "Kube-Mind-Dev") and click **Create**.

2.  **Enable the AI Platform API:**
    *   Once your project is created, navigate to the **APIs & Services > Library**.
    *   Search for "Vertex AI API" and enable it. This will give you access to Gemini and other models.

3.  **Create an API Key:**
    *   Navigate to **APIs & Services > Credentials**.
    *   Click **Create Credentials** and select **API key**.
    *   Copy the generated API key. You will need this for the `appsettings.Development.json` file.
    *   **Important:** For production environments, it is highly recommended to restrict the API key to prevent unauthorized use.

4.  **Configure `appsettings.Development.json`:**
    *   Open `brain/src/Kube-Mind.Brain.Api/appsettings.Development.json`.
    *   Update the `AIService` section as follows:

    ```json
    "AIService": {
      "Type": "Google",
      "ModelId": "gemini-1.5-pro", // Or any other supported Gemini model
      "ApiKey": "YOUR_GCP_API_KEY"
    },
    ```

    *   Replace `"YOUR_GCP_API_KEY"` with the API key you created.

## 2. Running Redis Locally with Docker

Redis is used as a vector database for historical context. The easiest way to run it locally is with Docker.

### Prerequisites

*   [Docker Desktop](https://www.docker.com/products/docker-desktop) installed and running.

### Steps

1.  **Pull the Redis Image:**
    *   Open your terminal and run the following command:
        ```bash
        docker pull redis
        ```

2.  **Run the Redis Container:**
    *   Execute the following command to start a Redis container:
        ```bash
        docker run --name kubemind-redis -p 6379:6379 -d redis
        ```
    *   This command does the following:
        *   `--name kubemind-redis`: Assigns a name to the container for easy reference.
        *   `-p 6379:6379`: Maps port 6379 on your local machine to port 6379 in the container.
        *   `-d`: Runs the container in detached mode (in the background).
        *   `redis`: Specifies the image to use.

3.  **Verify the Container is Running:**
    *   You can check the status of your container with:
        ```bash
        docker ps
        ```
    *   You should see `kubemind-redis` in the list of running containers.

4.  **Configuration:**
    *   The `appsettings.Development.json` is already configured to connect to `localhost:6379`, so no changes are needed.

## 3. GitHub App Setup for GitOps

Kube-Mind's GitOps functionality requires a GitHub App to create pull requests with proposed fixes.

### Steps

1.  **Create a New GitHub App:**
    *   Navigate to your GitHub profile settings > **Developer settings** > **GitHub Apps**.
    *   Click **New GitHub App**.

2.  **Configure the App:**
    *   **GitHub App name:** Give it a descriptive name (e.g., "Kube-Mind SRE Bot").
    *   **Homepage URL:** You can use your repository URL.
    *   **Webhook:** You can leave this inactive for now.
    *   **Repository permissions:**
        *   **Contents:** Set to `Read and write`. This is required to create branches and pull requests.
        *   **Pull requests:** Set to `Read and write`.
    *   **Where can this GitHub App be installed?:** Select `Only on this account`.

3.  **Generate a Private Key:**
    *   After creating the app, you will be taken to its settings page.
    *   Under **Private keys**, click **Generate a private key**.
    *   This will download a `.pem` file. **Treat this file like a password and keep it secure.**

4.  **Install the App:**
    *   Go to the **Install App** tab and install the app on the repository you want Kube-Mind to create pull requests in.

5.  **Get App ID and Installation ID:**
    *   **App ID:** You can find this on the app's general settings page.
    *   **Installation ID:** After installing the app on a repository, the URL will contain the installation ID (e.g., `github.com/settings/installations/INSTALLATION_ID`).

6.  **Configure `appsettings.Development.json`:**
    *   You will need to provide the App ID, Installation ID, and the contents of the private key file to the application. The current application uses a single GitHub token. You will need to modify the code to support authentication as a GitHub App.
    *   For now, you can use a Personal Access Token (PAT) for local development.
    *   Go to your GitHub profile settings > **Developer settings** > **Personal access tokens**.
    *   Generate a new token with `repo` scope.
    *   Update the `GitHub` section in `appsettings.Development.json`:

    ```json
    "GitHub": {
      "Token": "YOUR_GITHUB_PAT"
    }
    ```

    *   Replace `"YOUR_GITHUB_PAT"` with your Personal Access Token.
