using Robust.Client;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._Maid.Lobby.UI;

public sealed class LobbyServerListBox : BoxContainer
{
    // Сервера добавляются сюда. Название, описание, адрес подключения.
    private static readonly (string Name, string Desc, string Url)[] Servers =
    {
        ("BattleFront", "Сервер на нашем хосте", "ss14://battlefront.maidstation14.ru:1213"),
    };

    private readonly IGameController _gameController;
    private readonly List<Button> _connectButtons = new();

    public LobbyServerListBox()
    {
        _gameController = IoCManager.Resolve<IGameController>();
        Orientation = LayoutOrientation.Vertical;

        foreach (var (name, desc, url) in Servers)
        {
            AddServerInfo(name, desc, url);
        }
    }

    private void AddServerInfo(string serverName, string description, string url)
    {
        var serverBox = new BoxContainer
        {
            Orientation = LayoutOrientation.Horizontal,
        };

        var nameAndDescriptionBox = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
        };

        var serverNameLabel = new Label
        {
            Text = serverName,
            MinWidth = 200
        };

        var descriptionLabel = new RichTextLabel
        {
            MaxWidth = 500
        };
        descriptionLabel.SetMessage(FormattedMessage.FromMarkup(description));

        var buttonBox = new BoxContainer
        {
            Orientation = LayoutOrientation.Vertical,
            HorizontalExpand = true,
            HorizontalAlignment = HAlignment.Right
        };

        var connectButton = new Button
        {
            Text = Loc.GetString("ui-lobby-server-list-connect")
        };

        _connectButtons.Add(connectButton);

        connectButton.OnPressed += _ =>
        {
            _gameController.Redial(url, "Connecting to another server...");

            foreach (var button in _connectButtons)
            {
                button.Disabled = true;
            }
        };

        buttonBox.AddChild(connectButton);

        nameAndDescriptionBox.AddChild(serverNameLabel);
        nameAndDescriptionBox.AddChild(descriptionLabel);

        serverBox.AddChild(nameAndDescriptionBox);
        serverBox.AddChild(buttonBox);

        AddChild(serverBox);
    }
}
