using HighSchoolEcosystem.Models;
using HighSchoolEcosystem.Services;
using HighSchoolEcosystem.Services.Interfaces;
using HighSchoolEcosystem.ViewModels;

namespace HighSchoolEcosystem.Views;

public partial class ClassChatPage : ContentPage
{
    /// <summary>
    /// How often to look for new messages while the page is open. Each check
    /// costs at least one Firestore read even when nothing is new, so this is
    /// a balance: 5 s feels live enough for a class chat without a thirty-pupil
    /// class burning through reads. Polling stops the moment the page closes.
    /// </summary>
    private static readonly TimeSpan PollInterval = TimeSpan.FromSeconds(5);

    private readonly ClassChatViewModel _viewModel;
    private CancellationTokenSource? _polling;

    public ClassChatPage(ClassChatViewModel viewModel)
    {
        InitializeComponent();
        BindingContext = _viewModel = viewModel;
        _viewModel.MessagesAdded += (_, _) => ScrollToNewest();
    }

    /// <summary>Called before pushing; DI can't supply the class.</summary>
    public void Load(string classId, string className) => _viewModel.Initialise(classId, className);

    protected override async void OnAppearing()
    {
        base.OnAppearing();
        await _viewModel.LoadAsync();
        StartPolling();
    }

    protected override void OnDisappearing()
    {
        base.OnDisappearing();
        // Stop as soon as nobody's looking: polling a hidden page is pure cost.
        _polling?.Cancel();
        _polling = null;
        _viewModel.MarkRead();
    }

    private void StartPolling()
    {
        _polling?.Cancel();
        _polling = new CancellationTokenSource();
        _ = PollLoopAsync(_polling.Token);
    }

    // Started from the UI thread, so each await resumes there and the message
    // list is only ever touched on the UI thread.
    private async Task PollLoopAsync(CancellationToken token)
    {
        using var timer = new PeriodicTimer(PollInterval);
        try
        {
            while (await timer.WaitForNextTickAsync(token))
            {
                try { await _viewModel.PollAsync(token); }
                catch (OperationCanceledException) when (token.IsCancellationRequested) { return; }
                catch (Exception ex)
                {
                    // A dropped connection shouldn't end the chat; the next tick
                    // tries again.
                    CrashLogger.Log("ClassChat.Poll", ex);
                }
            }
        }
        catch (OperationCanceledException) { }
    }

    private void ScrollToNewest()
    {
        if (_viewModel.Messages.Count == 0) return;
        try
        {
            MessageList.ScrollTo(_viewModel.Messages.Count - 1, position: ScrollToPosition.End, animate: false);
        }
        catch (Exception ex)
        {
            CrashLogger.Log("ClassChat.Scroll", ex);
        }
    }

    /// <summary>
    /// Tapping a message offers what you can do with it: open its picture or
    /// file, and remove it if it's yours (or any, for the teacher). With only
    /// one option it just does it.
    /// </summary>
    private async void OnMessageTapped(object? sender, TappedEventArgs e)
    {
        if ((sender as BindableObject)?.BindingContext is not ChatMessageRow row) return;
        if (!row.HasAttachment && !row.CanDelete) return;

        try
        {
            if (row.HasAttachment && !row.CanDelete)
            {
                await OpenAttachmentAsync(row);
                return;
            }

            const string remove = "Remove message";
            string[] options = row.HasAttachment ? [row.OpenLabel] : [];
            var title = row.IsMine ? "Your message" : $"Message from {row.AuthorLabel}";
            var choice = await DisplayActionSheetAsync(title, "Cancel", remove, options);

            if (choice == row.OpenLabel)
            {
                await OpenAttachmentAsync(row);
            }
            else if (choice == remove)
            {
                var sure = await DisplayAlertAsync(
                    "Remove this message?",
                    row.HasAttachment
                        ? "It and its attachment will disappear for everyone in the class. This can't be undone."
                        : "It will disappear for everyone in the class. This can't be undone.",
                    "Remove", "Keep it");

                if (sure) await _viewModel.DeleteAsync(row);
            }
        }
        catch (Exception ex)
        {
            CrashLogger.Log("ClassChat.MessageTapped", ex);
        }
    }

    /// <summary>
    /// Opens the picture or file with the device's own viewer (browser, photo
    /// viewer, PDF or Office app) — the same way course material files open.
    /// </summary>
    private async Task OpenAttachmentAsync(ChatMessageRow row)
    {
        try
        {
            await Launcher.Default.OpenAsync(new Uri(row.AttachmentUrl));
        }
        catch (Exception ex)
        {
            CrashLogger.Log("ClassChat.OpenAttachment", ex);
            await DisplayAlertAsync("Couldn't open that", "Check your connection and try again.", "OK");
        }
    }

    // ---- attaching ----

    // What the chat accepts, matching storage.rules. Checked here too so a
    // student learns a file won't go BEFORE waiting for it to upload.
    private static readonly Dictionary<string, string> ContentTypes = new(StringComparer.OrdinalIgnoreCase)
    {
        [".jpg"] = "image/jpeg", [".jpeg"] = "image/jpeg", [".png"] = "image/png",
        [".gif"] = "image/gif", [".webp"] = "image/webp", [".heic"] = "image/heic",
        [".pdf"] = "application/pdf", [".txt"] = "text/plain",
        [".doc"] = "application/msword",
        [".docx"] = "application/vnd.openxmlformats-officedocument.wordprocessingml.document",
        [".xls"] = "application/vnd.ms-excel",
        [".xlsx"] = "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet",
        [".ppt"] = "application/vnd.ms-powerpoint",
        [".pptx"] = "application/vnd.openxmlformats-officedocument.presentationml.presentation",
    };

    /// <summary>
    /// Picks a picture or a file. The bytes are read here and handed over: the
    /// picker is a platform UI API, and its stream doesn't reliably survive
    /// being held until Send.
    /// </summary>
    private async void OnAttachClicked(object? sender, EventArgs e)
    {
        try
        {
            const string picture = "Picture";
            const string file = "File (PDF, Word, Excel, PowerPoint, text)";
            var kind = await DisplayActionSheetAsync("Attach", "Cancel", null, picture, file);
            if (kind != picture && kind != file) return;

            var result = await FilePicker.Default.PickAsync(new PickOptions
            {
                PickerTitle = kind == picture ? "Choose a picture" : "Choose a file",
                FileTypes = kind == picture ? FilePickerFileType.Images : null
            });
            if (result is null) return;

            // Trust the extension over the picker's content type: some pickers
            // report none, or a generic one the rules would refuse.
            var extension = Path.GetExtension(result.FileName);
            if (!ContentTypes.TryGetValue(extension, out var contentType))
            {
                await DisplayAlertAsync("That file type can't be sent",
                    "You can send pictures, PDFs, Word, Excel and PowerPoint files, and text files.", "OK");
                return;
            }

            using var stream = await result.OpenReadAsync();
            using var buffer = new MemoryStream();
            await stream.CopyToAsync(buffer);
            var bytes = buffer.ToArray();

            if (bytes.LongLength > IClassChatService.MaxAttachmentBytes)
            {
                await DisplayAlertAsync("That file is too large",
                    $"Attachments can be up to 15 MB. This one is {bytes.LongLength / (1024.0 * 1024.0):0.#} MB.", "OK");
                return;
            }

            _viewModel.SetPendingAttachment(new ChatAttachment(bytes, result.FileName, contentType));
        }
        catch (Exception ex)
        {
            CrashLogger.Log("ClassChat.Attach", ex);
            await DisplayAlertAsync("Couldn't open that file", "Please try a different one.", "OK");
        }
    }
}
