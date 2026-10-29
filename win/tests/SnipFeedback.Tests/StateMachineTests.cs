using SnipFeedback.Core;
using static SnipFeedback.Core.SnipState;
using static SnipFeedback.Core.SnipEvent;
using static SnipFeedback.Core.SnipAction;

namespace SnipFeedback.Tests;

// requirements.md の状態遷移表・E-04〜E-07 をそのまま確かめる（AC-13）
public class StateMachineTests
{
    private static SnipStateMachine At(SnipState s) => new(s);

    [Theory]
    // 待機
    [InlineData(Idle, Hotkey, Selecting, new[] { GrabAndShowSelector })]
    [InlineData(Idle, TraySend, Idle, new[] { Send })]
    [InlineData(Idle, Enter, Idle, new SnipAction[0])]
    [InlineData(Idle, DelayElapsed, Idle, new SnipAction[0])]
    // 切り取り画面
    [InlineData(Selecting, Hotkey, Selecting, new SnipAction[0])]              // E-07
    [InlineData(Selecting, Delay, Delaying, new[] { HideAndStartDelay })]
    [InlineData(Selecting, Enter, Idle, new[] { Send, Close })]                // r2・r4: 切り取らずに送る
    [InlineData(Selecting, CtrlEnter, Selecting, new SnipAction[0])]
    [InlineData(Selecting, Esc, Idle, new[] { Close })]
    [InlineData(Selecting, TraySend, Selecting, new SnipAction[0])]
    // 遅延待ち
    [InlineData(Delaying, Hotkey, Selecting, new[] { CancelDelay, GrabAndShowSelector })]   // E-05
    [InlineData(Delaying, DelayElapsed, Selecting, new[] { GrabAndShowSelector })]
    [InlineData(Delaying, Delay, Delaying, new SnipAction[0])]
    [InlineData(Delaying, Esc, Delaying, new SnipAction[0])]                  // r3: 画面が無いので起きない（受け取っても何もしない）
    [InlineData(Delaying, TraySend, Delaying, new[] { Send })]
    // 編集画面
    [InlineData(Editing, Hotkey, Editing, new SnipAction[0])]                  // E-07
    [InlineData(Editing, Enter, Idle, new[] { AddToQueue, Send, Close })]      // r4: 加えて送る
    [InlineData(Editing, CtrlEnter, Idle, new[] { AddToQueue, Close })]        // r4: ためるだけ
    [InlineData(Editing, Esc, Idle, new[] { Discard, Close })]
    [InlineData(Editing, TraySend, Editing, new SnipAction[0])]
    [InlineData(Editing, Delay, Editing, new SnipAction[0])]                   // 編集画面の D は文字の入力
    public void 状態遷移表どおり(SnipState from, SnipEvent ev, SnipState to, SnipAction[] actions)
    {
        var m = At(from);
        var got = m.Handle(ev);
        Assert.Equal(to, m.State);
        Assert.Equal(actions, got);
    }

    [Fact]
    public void 切り取りは8画素以上で編集画面へ_未満は切り取り画面のまま_E04()
    {
        var m = At(Selecting);
        Assert.Empty(m.Cut(7, 100));
        Assert.Equal(Selecting, m.State);
        Assert.Empty(m.Cut(100, 7));
        Assert.Equal(Selecting, m.State);
        Assert.Equal(new[] { ShowEditor }, m.Cut(8, 8));
        Assert.Equal(Editing, m.State);
    }

    [Fact]
    public void 編集画面での切り取り_ドラッグ_は赤枠の追加_8画素未満は無視()
    {
        var m = At(Editing);
        Assert.Equal(new[] { AddBox }, m.Cut(20, 20));
        Assert.Empty(m.Cut(3, 20));
        Assert.Equal(Editing, m.State);
    }

    [Fact]
    public void 待機と遅延待ちでの切り取りは起きないので無視()
    {
        Assert.Empty(At(Idle).Cut(50, 50));
        Assert.Empty(At(Delaying).Cut(50, 50));
    }

    [Fact]
    public void 一覧に加えるのに失敗したら編集画面に戻る_E02()
    {
        var m = At(Editing);
        m.Handle(Enter);
        m.AddFailed();
        Assert.Equal(Editing, m.State);
    }
}
