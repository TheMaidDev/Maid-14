using Content.Client.Administration.Managers;
using Content.Shared._Maid.Administration.AhelpHistory;
using Content.Shared.Administration;
using Robust.Shared.Network;

namespace Content.Client._Maid.Administration.AhelpHistory;

public sealed class AhelpHistorySystem : EntitySystem
{
    [Dependency] private readonly IClientAdminManager _adminManager = default!;

    private AhelpHistoryWindow? _window;

    private NetUserId _target;
    private int _page;
    private bool _hasMore;

    private int _anchorSkip;
    private int? _anchorId;

    private int? _roundId;
    private DateTime? _from;
    private DateTime? _to;
    private string _text = string.Empty;

    private bool _awaitingResponse;
    private (int? AnchorId, bool ScrollBottom)? _pendingRequest;
    private bool _scrollNextBottom;

    private bool _awaitingAnchor;

    public override void Initialize()
    {
        base.Initialize();

        SubscribeNetworkEvent<OpenAhelpHistory>(OnOpen);
        SubscribeNetworkEvent<AhelpHistoryResponse>(OnResponse);

        _adminManager.AdminStatusUpdated += OnAdminStatusUpdated;
    }

    public override void Shutdown()
    {
        base.Shutdown();

        _adminManager.AdminStatusUpdated -= OnAdminStatusUpdated;
    }

    private void OnAdminStatusUpdated()
    {
        if (_window is not { Disposed: false })
            return;

        if (_adminManager.HasFlag(AdminFlags.Adminhelp))
            return;

        CloseWindow(startedByUs: true);
    }

    private void OnOpen(OpenAhelpHistory message)
    {
        CloseWindow(startedByUs: true);

        _window = new AhelpHistoryWindow
        {
            Title = Loc.GetString("ahelp-history-title", ("player", message.TargetName)),
        };
        _window.OpenCentered();

        _window.SetHeader(message.TargetName, message.Target);
        _window.ClearFilters();

        _window.FiltersApplied += OnFiltersApplied;
        _window.PageBackRequested += OnPageBackRequested;
        _window.PageNextRequested += OnPageNextRequested;
        _window.PageSizeChanged += OnPageSizeChanged;
        _window.MoveToRequested += OnMoveToRequested;
        _window.OnClose += OnWindowClosed;

        _target = message.Target;
        _page = 0;
        _hasMore = false;
        _anchorSkip = 0;
        _anchorId = null;
        _roundId = null;
        _from = null;
        _to = null;
        _text = string.Empty;
        _awaitingResponse = false;
        _pendingRequest = null;
        _scrollNextBottom = false;
        _awaitingAnchor = false;

        Request();
    }

    private void CloseWindow(bool startedByUs = false)
    {
        if (_window is { Disposed: false } window)
        {
            window.FiltersApplied -= OnFiltersApplied;
            window.PageBackRequested -= OnPageBackRequested;
            window.PageNextRequested -= OnPageNextRequested;
            window.PageSizeChanged -= OnPageSizeChanged;
            window.MoveToRequested -= OnMoveToRequested;
            window.OnClose -= OnWindowClosed;

            if (startedByUs)
                window.Close();
        }

        _window = null;
        _page = 0;
        _hasMore = false;
        _anchorSkip = 0;
        _anchorId = null;
        _awaitingResponse = false;
        _pendingRequest = null;
        _scrollNextBottom = false;
        _awaitingAnchor = false;
    }

    private void OnWindowClosed()
    {
        CloseWindow();
    }

    private void OnFiltersApplied()
    {
        if (_window is not { Disposed: false } window)
            return;

        if (!window.ParseFilters(out var roundId, out var from, out var to, out var text))
            return;

        _roundId = roundId;
        _from = from;
        _to = to;
        _text = text;
        _page = 0;
        ResetAnchor();

        Request();
    }

    private void OnPageBackRequested()
    {
        if (_awaitingResponse)
            return;

        var pageSize = _window?.PageSize ?? 200;

        var current = PageSkip(_page, pageSize);
        var previous = PageSkip(_page - 1, pageSize);
        if (previous >= current)
            return;

        _page--;
        Request(scrollBottom: true);
    }

    private void OnPageNextRequested()
    {
        if (_awaitingResponse || !_hasMore)
            return;

        _page++;
        Request();
    }

    private void OnPageSizeChanged(int size)
    {
        _page = 0;
        ResetAnchor();
        Request();
    }

    private void OnMoveToRequested(AhelpHistoryEntry entry)
    {
        _roundId = null;
        _from = null;
        _to = null;
        _text = string.Empty;
        _window?.ClearFilters();

        _page = 0;
        _anchorId = entry.Id;
        _anchorSkip = 0;

        Request(anchorId: entry.Id, scrollBottom: true);
    }

    private void ResetAnchor()
    {
        _anchorSkip = 0;
        _anchorId = null;
        _awaitingAnchor = false;

        if (_pendingRequest is { })
            _pendingRequest = ((int?) null, false);
    }

    private bool HasActiveFilters()
    {
        return _roundId is not null
               || _from is not null
               || _to is not null
               || !string.IsNullOrWhiteSpace(_text);
    }

    private int PageSkip(int page, int pageSize)
    {
        return Math.Max(0, _anchorSkip + page * pageSize);
    }

    private void Request(int? anchorId = null, bool scrollBottom = false)
    {
        if (_awaitingResponse)
        {
            _pendingRequest = (anchorId ?? _pendingRequest?.AnchorId, scrollBottom);
            return;
        }

        var pending = _pendingRequest;
        _pendingRequest = null;

        var anchor = anchorId ?? pending?.AnchorId;
        var bottom = pending?.ScrollBottom ?? scrollBottom;

        _awaitingResponse = true;
        _awaitingAnchor = anchor is not null;
        _scrollNextBottom = bottom;
        _window?.SetLoading();

        RaiseNetworkEvent(new AhelpHistoryRequest
        {
            Target = _target,
            Limit = _window?.PageSize ?? 200,
            Skip = anchor is not null ? 0 : PageSkip(_page, _window?.PageSize ?? 200),
            AnchorId = anchor,
            RoundId = _roundId,
            From = _from,
            To = _to,
            Text = string.IsNullOrWhiteSpace(_text) ? null : _text,
        });
    }

    private void OnResponse(AhelpHistoryResponse response)
    {
        if (response.Target != _target)
            return;

        if (_window is not { Disposed: false } window)
            return;

        var pageSize = window.PageSize;
        var wasAnchored = _awaitingAnchor;
        var scrollBottom = _scrollNextBottom;

        _scrollNextBottom = false;

        if (_awaitingAnchor)
        {
            _awaitingAnchor = false;
            _anchorSkip = response.AnchorSkip;
            _page = 0;
        }

        _awaitingResponse = false;

        if (_pendingRequest is { } pending)
        {
            _pendingRequest = null;
            Request(pending.AnchorId, pending.ScrollBottom);
            return;
        }

        if (response.Error is { } error)
        {
            if (wasAnchored)
                ResetAnchor();

            window.ShowError(error);
            return;
        }

        _hasMore = response.HasMore;
        window.SetEntries(
            response.Entries,
            _hasMore,
            PageSkip(_page, pageSize),
            _anchorId,
            showMoveTo: HasActiveFilters());

        if (wasAnchored)
            scrollBottom = response.Entries.Count > 0 && response.Entries[^1].Id == _anchorId;

        if (scrollBottom)
            window.RequestScrollBottom();
        else
            window.RequestScrollTop();
    }
}
