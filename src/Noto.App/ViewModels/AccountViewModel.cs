using System.Collections.ObjectModel;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Noto.App.Services;
using Noto.Sync;

namespace Noto.App.ViewModels;

public sealed record ConnectionRow(Guid Id, string Provider, string Label);

// Settings → Account and sync, and connected apps. Holds form state only; AccountService does the work.
public sealed partial class AccountViewModel : ObservableObject
{
    readonly AccountService _account;

    public AccountViewModel(AccountService account)
    {
        _account = account;
        ServerText = account.ServerText ?? "";
        TokenOptions = account.TokenOptions;
        SelectedTokenOption = TokenOptions.FirstOrDefault();
        RefreshSignedIn();
    }

    public IReadOnlyList<TokenOption> TokenOptions { get; }
    public ObservableCollection<OAuthOption> OAuthOptions { get; } = [];
    public ObservableCollection<ConnectionRow> Connections { get; } = [];

    public bool HasOAuthOptions => OAuthOptions.Count > 0;

    [ObservableProperty]
    string _oAuthSetupHint = "";

    CancellationTokenSource? _oauthCancel;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(ShowOAuthSite))]
    [NotifyCanExecuteChangedFor(nameof(ConnectOAuthCommand))]
    OAuthOption? _selectedOAuthOption;

    [ObservableProperty]
    string _connectOAuthSite = "";

    public bool ShowOAuthSite => SelectedOAuthOption?.ShowSite == true;

    [ObservableProperty]
    [NotifyCanExecuteChangedFor(nameof(ConnectOAuthCommand), nameof(CancelOAuthCommand))]
    bool _isConnecting;

    [ObservableProperty]
    string _serverText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSignIn))]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand), nameof(SignUpCommand))]
    string _email = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanSignIn))]
    [NotifyCanExecuteChangedFor(nameof(SignInCommand), nameof(SignUpCommand))]
    string _password = "";

    [ObservableProperty]
    string _inviteCode = "";

    [ObservableProperty]
    bool _isSignedIn;

    [ObservableProperty]
    string _signedInText = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasAccountStatus))]
    string? _accountStatus;

    [ObservableProperty]
    bool _accountStatusIsError;

    [ObservableProperty]
    [NotifyPropertyChangedFor(
        nameof(ShowSite),
        nameof(ShowSiteRequired),
        nameof(ShowEmail),
        nameof(CanConnect)
    )]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    TokenOption? _selectedTokenOption;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConnect))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    string _connectSite = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConnect))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    string _connectEmail = "";

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(CanConnect))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    string _token = "";

    [ObservableProperty]
    bool _hasConnections;

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasConnectionStatus))]
    string? _connectionStatus;

    [ObservableProperty]
    bool _connectionStatusIsError;

    public bool HasAccountStatus => !string.IsNullOrEmpty(AccountStatus);
    public bool HasConnectionStatus => !string.IsNullOrEmpty(ConnectionStatus);

    // Sign-in and sign-up both need an email and a password; nothing is sent until the form is complete.
    public bool CanSignIn => !string.IsNullOrWhiteSpace(Email) && Password.Length > 0;

    public bool ShowSite => SelectedTokenOption?.Site is not null and not SiteField.None;

    public bool ShowSiteRequired => SelectedTokenOption?.Site == SiteField.Required;

    public bool ShowEmail => SelectedTokenOption?.NeedsEmail == true;

    // Each field the chosen option needs must be filled before Connect is enabled.
    public bool CanConnect =>
        SelectedTokenOption is { } o
        && !string.IsNullOrWhiteSpace(Token)
        && (o.Site != SiteField.Required || !string.IsNullOrWhiteSpace(ConnectSite))
        && (!o.NeedsEmail || !string.IsNullOrWhiteSpace(ConnectEmail));

    // Loads the connected-apps list. Called when Settings opens.
    // A failed read shows a message in the connections section; it must not take down the Settings page.
    public async Task LoadAsync()
    {
        try
        {
            await ReloadConnectionsAsync();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            ConnectionStatus = $"Couldn't load connected apps: {Message(e)}";
            ConnectionStatusIsError = true;
        }
        await LoadOAuthOptionsAsync();
    }

    // Asks the signed-in server which providers it can sign people in with.
    async Task LoadOAuthOptionsAsync()
    {
        try
        {
            var options = await _account.LoadOAuthOptionsAsync(CancellationToken.None);
            var configured = options.Where(o => o.Configured).ToList();
            OAuthOptions.Clear();
            foreach (var option in configured)
                OAuthOptions.Add(option);
            SelectedOAuthOption = configured.FirstOrDefault();
            OAuthSetupHint = configured.Count > 0 ? "" : SetupHint();
            OnPropertyChanged(nameof(HasOAuthOptions));
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            OAuthOptions.Clear();
            SelectedOAuthOption = null;
            OAuthSetupHint = $"Couldn't load sign-in options: {Message(e)}";
            OnPropertyChanged(nameof(HasOAuthOptions));
        }
    }

    // Says what is missing: a server connection, or provider credentials on that server.
    string SetupHint() =>
        _account.Account is null
            ? "Browser sign-in goes through your Noto server. Connect this device to it under Account and sync, or paste a token below."
            : "Your Noto server has no sign-in apps set up. Its administrator adds each provider's client ID and secret to the server's .env (docs/12-connected-apps-setup.md), or paste a token below.";

    [RelayCommand]
    void SaveServer()
    {
        try
        {
            _account.SetServer(ServerText);
            ServerText = _account.ServerText ?? "";
            AccountStatus = ServerText.Length == 0 ? "Server cleared." : "Server saved.";
            AccountStatusIsError = false;
        }
        catch (ArgumentException e)
        {
            AccountStatus = e.Message;
            AccountStatusIsError = true;
        }
    }

    [RelayCommand(CanExecute = nameof(CanSignIn))]
    Task SignUpAsync() =>
        RunAccountAsync(
            "Account created. You're signed in.",
            ct => _account.SignUpAsync(Email, Password, InviteCode, ct)
        );

    [RelayCommand(CanExecute = nameof(CanSignIn))]
    Task SignInAsync() =>
        RunAccountAsync("Signed in.", ct => _account.SignInAsync(Email, Password, ct));

    [RelayCommand]
    Task SignOutAsync() => RunAccountAsync("Signed out.", ct => _account.SignOutAsync(ct));

    [RelayCommand(CanExecute = nameof(CanConnect))]
    Task ConnectAsync() =>
        RunConnectionAsync(
            "Connected.",
            async ct =>
            {
                await _account.ConnectTokenAsync(
                    SelectedTokenOption!,
                    Token,
                    NullIfBlank(ConnectSite),
                    NullIfBlank(ConnectEmail),
                    ct
                );
                Token = "";
            }
        );

    [RelayCommand(CanExecute = nameof(CanConnectOAuth))]
    async Task ConnectOAuthAsync()
    {
        var option = SelectedOAuthOption!;
        var cancel = _oauthCancel = new CancellationTokenSource();
        IsConnecting = true;
        ConnectionStatus = $"Finish signing in to {option.ProviderName} in your browser.";
        ConnectionStatusIsError = false;
        try
        {
            // A site typed for one provider must not follow the user to another one.
            var site = option.ShowSite ? NullIfBlank(ConnectOAuthSite) : null;
            await RunConnectionAsync(
                "Connected.",
                ct => _account.ConnectOAuthAsync(option.ProviderId, site, ct),
                cancel.Token
            );
        }
        finally
        {
            _oauthCancel = null;
            IsConnecting = false;
            cancel.Dispose();
        }
    }

    bool CanConnectOAuth => SelectedOAuthOption is not null && !IsConnecting;

    [RelayCommand(CanExecute = nameof(IsConnecting))]
    void CancelOAuth() => _oauthCancel?.Cancel();

    [RelayCommand]
    Task DisconnectAsync(ConnectionRow row) =>
        RunConnectionAsync("Disconnected.", ct => _account.DisconnectAsync(row.Id, ct));

    async Task RunAccountAsync(string success, Func<CancellationToken, Task> work)
    {
        try
        {
            await work(CancellationToken.None);
            Password = "";
            InviteCode = "";
            AccountStatus = success;
            AccountStatusIsError = false;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            AccountStatus = Message(e);
            AccountStatusIsError = true;
        }
        RefreshSignedIn();
        await ReloadConnectionsAsync();
        await LoadOAuthOptionsAsync();
    }

    // Catches everything: an unhandled exception in an async command takes the whole app down.
    async Task RunConnectionAsync(
        string success,
        Func<CancellationToken, Task> work,
        CancellationToken ct = default
    )
    {
        try
        {
            await work(ct);
            ConnectionStatus = success;
            ConnectionStatusIsError = false;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested)
        {
            ConnectionStatus = "Sign-in cancelled.";
            ConnectionStatusIsError = false;
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            ConnectionStatus = Message(e);
            ConnectionStatusIsError = true;
        }
        try
        {
            await ReloadConnectionsAsync();
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            ConnectionStatus = Message(e);
            ConnectionStatusIsError = true;
        }
    }

    void RefreshSignedIn()
    {
        IsSignedIn = _account.Account is not null;
        SignedInText = _account.Account is { } a ? $"{a.Email} · {a.Server.Host}" : "";
    }

    async Task ReloadConnectionsAsync()
    {
        var names = TokenOptions
            .GroupBy(o => o.ProviderId)
            .ToDictionary(g => g.Key, g => g.First().ProviderName);
        var rows = await _account.ListConnectionsAsync();
        Connections.Clear();
        foreach (var c in rows)
            Connections.Add(
                new ConnectionRow(
                    c.Id,
                    names.GetValueOrDefault(c.ProviderId, c.ProviderId),
                    c.DisplayLabel
                )
            );
        HasConnections = Connections.Count > 0;
    }

    static string? NullIfBlank(string s) => string.IsNullOrWhiteSpace(s) ? null : s.Trim();

    static string Message(Exception e) =>
        e is ServerAuthException s ? Friendly(s)
        : string.IsNullOrWhiteSpace(e.Message) ? "Something went wrong. Try again."
        : e.Message;

    // Plain-language messages for the server's error codes; anything else shows the server's title.
    static string Friendly(ServerAuthException e) =>
        e.Code switch
        {
            "INVALID_CREDENTIALS" => "Wrong email or password.",
            "EMAIL_TAKEN" => "That email already has an account on this server.",
            "INVALID_INVITE" => "That invite code isn't accepted by this server.",
            "REGISTRATION_CLOSED" => "This server isn't accepting new accounts.",
            "NETWORK" => "Can't reach the server. Check the address and that it is running.",
            _ => e.Message,
        };
}
