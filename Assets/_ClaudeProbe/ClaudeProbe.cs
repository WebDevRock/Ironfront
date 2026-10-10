using UnityEngine;
public static class ClaudeProbe
{
    public static string Ping() { Debug.Log("ClaudeProbe: hello from Claude"); return "pong " + Application.unityVersion; }
}
