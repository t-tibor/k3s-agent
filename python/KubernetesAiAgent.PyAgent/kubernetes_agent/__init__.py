"""Kubernetes AI Agent — Python port on Microsoft Agent Framework.

Mirrors the .NET agent (``KubernetesAiAgent.NetAgent``) but exposes only the AG-UI protocol: Microsoft
Agent Framework for Python has no server-side equivalent of the .NET agent's
``MapOpenAIChatCompletions`` (there is no OpenAI-compatible chat-completions hosting extension for
Python yet), so there is no ``/v1/chat/completions`` or ``/v1/models`` endpoint here.
"""
