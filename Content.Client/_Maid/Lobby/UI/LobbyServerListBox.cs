using Robust.Client;
using Robust.Client.UserInterface.Controls;
using Robust.Shared.Utility;

namespace Content.Client._Maid.Lobby.UI;

public sealed class LobbyServerListBox : BoxContainer
{
    // Сервера добавляются сюда. Название, локализация описания, адрес подключения.
    private static readonly (string Name, string DescLoc, string Url)[] Servers =
    {
        ("BattleFront", "ui-lobby-server-battlefront-desc", "ss14://battlefront.maidstation14.ru:1213"),
    };

    private readonly IGameController _gameController;
    private readonly List<Button> _connectButtons = new();

    public LobbyServerListBox()
    {
        _gameController = IoCManager.Resolve<IGameController>();
        Orientation = LayoutOrientation.Vertical;

        foreach (var (name, descLoc, url) in Servers)
        {
            AddServerInfo(name, descLoc, url);
        }
    }

    private void AddServerInfo(string serverName, string descriptionLoc, string url)
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
        descriptionLabel.SetMessage(FormattedMessage.FromMarkup(Loc.GetString(descriptionLoc)));

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
            _gameController.Redial(url, Loc.GetString("ui-lobby-server-list-connecting"));

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
