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
        TokenProviders = account.TokenProviders;
        SelectedTokenProvider = TokenProviders.FirstOrDefault();
        RefreshSignedIn();
    }

    public IReadOnlyList<TokenProvider> TokenProviders { get; }
    public ObservableCollection<ConnectionRow> Connections { get; } = [];

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
    [NotifyPropertyChangedFor(nameof(CanConnect))]
    [NotifyCanExecuteChangedFor(nameof(ConnectCommand))]
    TokenProvider? _selectedTokenProvider;

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

    public bool CanConnect =>
        SelectedTokenProvider is not null && !string.IsNullOrWhiteSpace(Token);

    // Loads the connected-apps list. Called when Settings opens.
    public async Task LoadAsync() => await ReloadConnectionsAsync();

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
                await _account.ConnectTokenAsync(SelectedTokenProvider!.Id, Token, ct);
                Token = "";
            }
        );

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
        catch (Exception e) when (IsUserFacing(e))
        {
            AccountStatus = Message(e);
            AccountStatusIsError = true;
        }
        RefreshSignedIn();
        await ReloadConnectionsAsync();
    }

    async Task RunConnectionAsync(string success, Func<CancellationToken, Task> work)
    {
        try
        {
            await work(CancellationToken.None);
            ConnectionStatus = success;
            ConnectionStatusIsError = false;
        }
        catch (Exception e) when (IsUserFacing(e))
        {
            ConnectionStatus = Message(e);
            ConnectionStatusIsError = true;
        }
        await ReloadConnectionsAsync();
    }

    void RefreshSignedIn()
    {
        IsSignedIn = _account.Account is not null;
        SignedInText = _account.Account is { } a ? $"{a.Email} · {a.Server.Host}" : "";
    }

    async Task ReloadConnectionsAsync()
    {
        var names = TokenProviders.ToDictionary(p => p.Id, p => p.Name);
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

    static bool IsUserFacing(Exception e) =>
        e
            is ServerAuthException
                or ArgumentException
                or InvalidOperationException
                or HttpRequestException;

    static string Message(Exception e) => e is ServerAuthException s ? Friendly(s) : e.Message;

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
