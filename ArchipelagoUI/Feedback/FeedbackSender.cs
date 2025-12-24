using OriBFArchipelago.Core;
using OriBFArchipelago.MapTracker.Core;
using System.Collections;
using System.Collections.Generic;
using System.Text;
using UnityEngine;

public class FeedbackSender : MonoBehaviour
{
    private const string FEEDBACK_ENDPOINT = "https://ori-bf-feedback-mod.vercel.app/api/feedback";

    public void Initialize()
    {
    }

    public void SendFeedback(string userMessage)
    {
        ModLogger.Debug("Preparing to send feedback...");
        string systemInfo = CollectSystemInfo();
        string inventory = CollectInventory();
        string options = CollectOptions();
        string archipelagoData = CollectArchipelagoData();
        StartCoroutine(SendToAPI(userMessage, systemInfo, inventory, options, archipelagoData));
    }

    private IEnumerator SendToAPI(string message, string systemInfo, string inventory, string options, string archipelagoData)
    {


        message = "Message: \n" + message + "\n\n" +
                    "Archipelago Data:\n" + archipelagoData + "\n\n" +
                    "Inventory:\n" + inventory + "\n\n" +
                    "Options:\n" + options;

        var payload = new FeedbackPayload
        {
            message = message,
            deviceId = SystemInfo.deviceUniqueIdentifier,
            systemInfo = systemInfo,
        };

        string jsonData = JsonUtility.ToJson(payload);
        byte[] bodyRaw = Encoding.UTF8.GetBytes(jsonData);

        ModLogger.Debug($"Sending to: {FEEDBACK_ENDPOINT}");

        Dictionary<string, string> headers = new Dictionary<string, string>();
        headers.Add("Content-Type", "application/json");

        WWW www = new WWW(FEEDBACK_ENDPOINT, bodyRaw, headers);

        yield return www;

        if (!string.IsNullOrEmpty(www.error))
        {
            ModLogger.Error($"Failed to send feedback: {www.error}");
        }
        else
        {
            ModLogger.Debug("Feedback sent successfully!");
            ModLogger.Debug($"Response: {www.text}");
        }
    }

    private string CollectSystemInfo()
    {
        ModLogger.Debug("Collecting system info...");

        StringBuilder sb = new StringBuilder();
        sb.AppendLine($"Game: {Application.productName}");
        sb.AppendLine($"Version: {Application.version}");
        sb.AppendLine($"Unity: {Application.unityVersion}");
        sb.AppendLine($"Platform: {Application.platform}");
        sb.AppendLine($"OS: {SystemInfo.operatingSystem}");
        sb.AppendLine($"CPU: {SystemInfo.processorType}");
        sb.AppendLine($"RAM: {SystemInfo.systemMemorySize} MB");
        sb.AppendLine($"GPU: {SystemInfo.graphicsDeviceName}");

        return sb.ToString();
    }
    private string CollectInventory()
    {
        ModLogger.Debug("Collecting inventory...");
        if (RandomizerManager.Receiver == null)
            return "No inventory data available.";
        
        var inventory = RandomizerManager.Receiver.GetAllItems();
        StringBuilder sb = new StringBuilder();
        foreach (var item in inventory)
        {
            sb.AppendLine($"{item.Key}: {item.Value}");
        }
        return sb.ToString();
    }

    private string CollectOptions()
    {
        ModLogger.Debug("Collecting options...");
        if (RandomizerManager.Options == null)
            return "No options data available.";


        var options = RandomizerManager.Options.GetAllOptions();
        StringBuilder sb = new StringBuilder();
        foreach (var option in options)
        {
            sb.AppendLine($"{option.Key}: {option.Value}");
        }
        return sb.ToString();
    }
    private string CollectArchipelagoData()
    {
        ModLogger.Debug("Collecting archipelago data...");
        if (RandomizerManager.Connection == null)
            return "Not connected to Archipelago.";

        var sb = new StringBuilder();
        sb.AppendLine($"Slotname: {RandomizerManager.Connection?.GetSlotName() ?? "No slot name"}");
        return sb.ToString();
    }
    [System.Serializable]
    private class FeedbackPayload
    {
        public string message;
        public string deviceId;
        public string systemInfo;
    }
}