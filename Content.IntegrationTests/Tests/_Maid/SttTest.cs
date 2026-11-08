using Content.Client._Maid.Stt;
using Content.Client.UserInterface.Systems.Chat.Widgets;
using Content.Shared._Maid.CVars;
using Robust.Client.UserInterface;
using Robust.Shared.Configuration;
using Robust.Shared.ContentPack;
using Robust.Shared.Utility;

namespace Content.IntegrationTests.Tests._Maid;

[TestFixture]
[TestOf(typeof(SttSystem))]
public sealed class SttTest
{
    private static readonly ResPath Inbox = new("/stt/inbox");

    [Test]
    public async Task SttInboxTest()
    {
        await using var pair = await PoolManager.GetServerClient(new PoolSettings
        {
            Connected = true,
            DummyTicker = false
        });
        var client = pair.Client;

        var res = client.ResolveDependency<IResourceManager>();
        var cfg = client.ResolveDependency<IConfigurationManager>();
        var ui = client.ResolveDependency<IUserInterfaceManager>();

        ChatBox GetBox() => ui.ActiveScreen!.GetWidget<ChatBox>() ?? ui.ActiveScreen!.GetWidget<ResizableChatBox>()!;

        void Drop(string name, string text)
        {
            res.UserData.CreateDir(Inbox);
            using var w = res.UserData.OpenWriteText(Inbox / name);
            w.Write(text);
        }

        // Review mode: text lands in the chat input and is not submitted.
        await client.WaitPost(() =>
        {
            cfg.SetCVar(MaidCVars.SttEnabled, true);
            cfg.SetCVar(MaidCVars.SttConfirm, true);
            cfg.SetCVar(MaidCVars.SttMaxLength, 20);
            GetBox().ChatInput.Input.Clear();
            Drop("1.txt", "привет\nмир");
            Drop("2.txt.tmp", "partial");
        });
        await pair.RunSeconds(1);

        await client.WaitAssertion(() =>
        {
            Assert.That(GetBox().ChatInput.Input.Text, Is.EqualTo("привет мир"));
            Assert.That(res.UserData.Exists(Inbox / "1.txt"), Is.False, "consumed file must be deleted");
            Assert.That(res.UserData.Exists(Inbox / "2.txt.tmp"), Is.True, ".tmp files must be left alone");
        });

        // Length cap.
        await client.WaitPost(() =>
        {
            res.UserData.Delete(Inbox / "2.txt.tmp");
            Drop("3.txt", new string('a', 100));
        });
        await pair.RunSeconds(1);

        await client.WaitAssertion(() =>
        {
            Assert.That(GetBox().ChatInput.Input.Text, Is.EqualTo(new string('a', 20)));
        });

        // Disabled: nothing is consumed.
        await client.WaitPost(() =>
        {
            cfg.SetCVar(MaidCVars.SttEnabled, false);
            GetBox().ChatInput.Input.Clear();
            Drop("4.txt", "ignored");
        });
        await pair.RunSeconds(1);

        await client.WaitAssertion(() =>
        {
            Assert.That(GetBox().ChatInput.Input.Text, Is.Empty);
            Assert.That(res.UserData.Exists(Inbox / "4.txt"), Is.True);
        });

        // Immediate mode: text is submitted, so the input ends up empty and the file is consumed.
        await client.WaitPost(() =>
        {
            cfg.SetCVar(MaidCVars.SttEnabled, true);
            cfg.SetCVar(MaidCVars.SttConfirm, false);
        });
        await pair.RunSeconds(1);

        await client.WaitAssertion(() =>
        {
            Assert.That(res.UserData.Exists(Inbox / "4.txt"), Is.False);
            Assert.That(GetBox().ChatInput.Input.Text, Is.Empty, "submitted text clears the input");
        });

        await pair.CleanReturnAsync();
    }
}
