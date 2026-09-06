"""Entrypoint for the Aspire AppHost (``AddPythonApp("agent", "../KubernetesAiAgent.PyAgent", "main.py")``)
and for standalone runs (``uv run main.py``). Binds uvicorn to ``$PORT`` — the env var Aspire's
``WithHttpEndpoint(port: 5192, env: "PORT")`` sets; 5192 is also the fallback the .NET agent used, and
what the webui's dev fallback expects, so both agents are reachable at the same URL when swapped in.
"""

import os

import uvicorn

from kubernetes_agent.app import create_app

app = create_app()

if __name__ == "__main__":
    uvicorn.run(app, host="0.0.0.0", port=int(os.environ.get("PORT", "5192")))
