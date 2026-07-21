using System.Net;
using System.Text.RegularExpressions;
using CommunityToolkit.Maui.Views;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Serilog;
using TheBleedingDeacons.Intergroup.Register.Support;

namespace TheBleedingDeacons.Intergroup.Register.ViewModels
{
    /// <summary>
    /// Backs <see cref="Views.AcceptTermsPopup"/>. Mirrors
    /// <see cref="CountdownPopupViewModel"/> in shape (popup reference + bound
    /// Title/Message), but instead of an auto-advancing timer it exposes
    /// Accept/Decline commands. The user's choice is forwarded through the
    /// supplied <see cref="TaskCompletionSource{Boolean}"/> so the calling
    /// service can <c>await</c> a <see cref="Task{Boolean}"/> result.
    ///
    /// <para><b>Body source.</b> The body passed in is the cached upstream
    /// privacy-policy HTML from Scrutiny (<c>wpautop</c>-formatted). The
    /// popup renders into a plain <see cref="Microsoft.Maui.Controls.Label"/>,
    /// so the constructor strips tags and decodes entities to a plain-text
    /// shape that preserves paragraph breaks via blank lines. <c>&lt;p&gt;</c>
    /// boundaries and <c>&lt;br&gt;</c> tags become newlines; everything else
    /// (lists, links, inline emphasis) is dropped. Faithful HTML rendering
    /// would mean a <c>WebView</c>; we deliberately accept the loss of
    /// structure here in exchange for the simpler control.</para>
    /// </summary>
    public partial class AcceptTermsPopupViewModel : ObservableObject
    {
        private static readonly ILogger Logger = AppLogger.ForContext<AcceptTermsPopupViewModel>();

        // Guards the HTML-flattening regexes against pathological (ReDoS) input.
        private static readonly TimeSpan RegexTimeout = TimeSpan.FromSeconds(2);

        // Block-level tags whose boundaries should become a paragraph break
        // when flattened to plain text. Anchored on opening or closing form
        // so </p> and <p ...> both match. Compiled once because the popup
        // can be opened many times during a meeting.
        private static readonly Regex BlockBoundary = new(
            @"</?(p|div|h[1-6]|li|tr|br\s*/?)\b[^>]*>",
            RegexOptions.IgnoreCase | RegexOptions.Compiled, RegexTimeout);

        // Anything else that looks like a tag — strip silently.
        private static readonly Regex AnyTag = new(
            @"<[^>]+>",
            RegexOptions.Compiled, RegexTimeout);

        // Collapse 3+ consecutive newlines down to a paragraph break.
        // wpautop tends to emit double-blank-line runs around block
        // elements; the BlockBoundary substitution above can stack on top
        // of those, so without this the popup ends up with great gaps.
        private static readonly Regex BlankLineRun = new(
            @"(\r?\n\s*){3,}",
            RegexOptions.Compiled, RegexTimeout);

        private readonly Popup _popup;
        private readonly TaskCompletionSource<bool> _resultTcs;

        [ObservableProperty]
        private string _title = string.Empty;

        [ObservableProperty]
        private string _message = string.Empty;

        // Two-way bound to the consent checkbox below the policy text.
        // Combined with HasScrolledToEnd to gate AcceptCommand.
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(AcceptCommand))]
        private bool _hasAgreed;

        // Set by the popup's code-behind when the policy ScrollView reaches
        // the bottom (or when the body is short enough that no scrolling is
        // required). The user cannot agree to terms they haven't seen, so
        // this is required in addition to ticking the checkbox.
        [ObservableProperty]
        [NotifyCanExecuteChangedFor(nameof(AcceptCommand))]
        private bool _hasScrolledToEnd;

        /// <summary>
        /// Gate for <see cref="AcceptCommand"/>. The user must both tick the
        /// agreement checkbox AND have scrolled to the end of the policy.
        /// </summary>
        private bool CanAccept => HasAgreed && HasScrolledToEnd;

        public AcceptTermsPopupViewModel(
            Popup popup,
            string title,
            string message,
            TaskCompletionSource<bool> resultTcs)
        {
            _popup = popup;
            _resultTcs = resultTcs;
            _title = title;
            _message = StripHtmlForLabel(message);
        }

        /// <summary>
        /// Flattens an HTML body to plain text suitable for binding to a
        /// <see cref="Microsoft.Maui.Controls.Label"/>. Block boundaries
        /// become newlines; remaining tags are removed; HTML entities are
        /// decoded; runs of blank lines are collapsed to a single paragraph
        /// break. Null/empty input returns <see cref="string.Empty"/>.
        /// </summary>
        internal static string StripHtmlForLabel(string? html)
        {
            if (string.IsNullOrWhiteSpace(html)) return string.Empty;

            // Order matters: convert block boundaries to newlines BEFORE
            // stripping the rest, otherwise <p>foo</p><p>bar</p> would
            // collapse to "foobar".
            var withBreaks = BlockBoundary.Replace(html, "\n");
            var stripped = AnyTag.Replace(withBreaks, string.Empty);
            var decoded = WebUtility.HtmlDecode(stripped);
            var normalised = BlankLineRun.Replace(decoded, "\n\n");
            return normalised.Trim();
        }

        [RelayCommand(CanExecute = nameof(CanAccept))]
        private async Task Accept()
        {
            Logger.Information("Compliance popup: user accepted");
            _resultTcs.TrySetResult(true);
            await _popup.CloseAsync();
        }

        [RelayCommand]
        private async Task Decline()
        {
            Logger.Information("Compliance popup: user declined");
            _resultTcs.TrySetResult(false);
            await _popup.CloseAsync();
        }
    }
}
