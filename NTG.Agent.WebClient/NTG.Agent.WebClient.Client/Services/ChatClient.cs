using NTG.Agent.Common.Dtos.Chats;
using System.Net.Http.Json;
using NTG.Agent.WebClient.Client.Dtos;
using System.Globalization;

namespace NTG.Agent.WebClient.Client.Services;

public class ChatClient(HttpClient httpClient)
{
    private const string REQUEST_URI = "/api/agents/chat";

    private const int maxFileSize = 50 * 1024 * 1024; // 50 MB

    public async Task<IAsyncEnumerable<PromptResponse>> InvokeStreamAsync(PromptRequest<UploadItemClient> request)
    {
        using var form = new MultipartFormDataContent();

        // --- 1️ Add Documents first ---
        if (request.Documents != null)
        {
            for (int i = 0; i < request.Documents.Count(); i++)
            {
                var doc = request.Documents.ElementAt(i);

                // File content
                if (doc.Content != null)
                {
                    form.Add(doc.Content, $"{nameof(request.Documents)}[{i}].{nameof(doc.Content)}", doc.Name);
                }

                // Scalars inside document
                form.Add(new StringContent(doc.Name ?? ""), $"{nameof(request.Documents)}[{i}].{nameof(doc.Name)}");
                form.Add(new StringContent(doc.Status.ToString()), $"{nameof(request.Documents)}[{i}].{nameof(doc.Status)}");
                form.Add(new StringContent(doc.Message ?? ""), $"{nameof(request.Documents)}[{i}].{nameof(doc.Message)}");
                form.Add(new StringContent(doc.Progress.ToString(CultureInfo.InvariantCulture)), $"{nameof(request.Documents)}[{i}].{nameof(doc.Progress)}");
            }
        }

        // --- 2️ Add other scalar properties ---
        form.Add(new StringContent(request.Prompt ?? ""), nameof(request.Prompt));
        form.Add(new StringContent(request.ConversationId.ToString()), nameof(request.ConversationId));
        if (!string.IsNullOrEmpty(request.SessionId))
            form.Add(new StringContent(request.SessionId), nameof(request.SessionId));

        // --- 3️ Send request ---
        var response = await httpClient.PostAsync(REQUEST_URI, form);
        response.EnsureSuccessStatusCode();

        return response.Content.ReadFromJsonAsAsyncEnumerable<PromptResponse>()!;
    }



}
