namespace SnipFeedback.Core;

public enum SnipState { Idle, Selecting, Delaying, Editing }

// 切り取り（ドラッグ終了・クリック・Space）は大きさを伴うので Cut() で渡す
public enum SnipEvent { Hotkey, Delay, DelayElapsed, Enter, CtrlEnter, Esc, TraySend, History }

public enum SnipAction
{
    GrabAndShowSelector,   // 画面を取って切り取り画面を出す
    HideAndStartDelay,     // 切り取り画面を隠し、遅延を始める（R-05）
    CancelDelay,
    ShowEditor,            // 編集画面へ（R-07）
    AddBox,                // 赤枠を足す（R-08）
    AddToQueue,            // 一覧に加える（R-09・R-11）
    Send,                  // 送信（R-13・R-14）
    Discard,               // この切り取りを捨てる
    Close,                 // 画面を閉じる
    ShowHistory,           // 送った単位の履歴を出す（R-21。切り取り画面を閉じた後）
}

// requirements.md の状態遷移表をそのまま表にしたもの。画面に依らない（design.md Core）
public sealed class SnipStateMachine
{
    public const int MinSize = 8;   // E-04

    public SnipState State { get; private set; }

    public SnipStateMachine(SnipState initial = SnipState.Idle) => State = initial;

    private static readonly SnipAction[] None = Array.Empty<SnipAction>();

    public SnipAction[] Handle(SnipEvent e)
    {
        switch (State, e)
        {
            case (SnipState.Idle, SnipEvent.Hotkey):
                State = SnipState.Selecting; return new[] { SnipAction.GrabAndShowSelector };
            case (SnipState.Idle, SnipEvent.TraySend):
                return new[] { SnipAction.Send };

            case (SnipState.Selecting, SnipEvent.Delay):
                State = SnipState.Delaying; return new[] { SnipAction.HideAndStartDelay };
            case (SnipState.Selecting, SnipEvent.Enter):                       // r2・r4: 切り取らずに、ためた分を送る（R-14）
                State = SnipState.Idle; return new[] { SnipAction.Send, SnipAction.Close };
            case (SnipState.Selecting, SnipEvent.Esc):
                State = SnipState.Idle; return new[] { SnipAction.Close };
            case (SnipState.Selecting, SnipEvent.History):                     // r9: H で切り取り画面を閉じて履歴（R-21）
                State = SnipState.Idle; return new[] { SnipAction.Close, SnipAction.ShowHistory };

            case (SnipState.Delaying, SnipEvent.Hotkey):                       // E-05
                State = SnipState.Selecting; return new[] { SnipAction.CancelDelay, SnipAction.GrabAndShowSelector };
            case (SnipState.Delaying, SnipEvent.DelayElapsed):
                State = SnipState.Selecting; return new[] { SnipAction.GrabAndShowSelector };
            case (SnipState.Delaying, SnipEvent.TraySend):
                return new[] { SnipAction.Send };

            case (SnipState.Editing, SnipEvent.Enter):                         // r4: 加えて、ためた分ごと送る（R-09）
                State = SnipState.Idle; return new[] { SnipAction.AddToQueue, SnipAction.Send, SnipAction.Close };
            case (SnipState.Editing, SnipEvent.CtrlEnter):                     // r4: ためるだけ
                State = SnipState.Idle; return new[] { SnipAction.AddToQueue, SnipAction.Close };
            case (SnipState.Editing, SnipEvent.Esc):
                State = SnipState.Idle; return new[] { SnipAction.Discard, SnipAction.Close };

            default:
                // 表の「何もしない」と「—（起きない）」。E-07（表示中の起動キー）もここ
                return None;
        }
    }

    // 切り取り画面ではその範囲を切り取り、編集画面では赤枠を足す。8 画素未満は無視（E-04）
    public SnipAction[] Cut(int width, int height)
    {
        if (width < MinSize || height < MinSize) return None;
        switch (State)
        {
            case SnipState.Selecting:
                State = SnipState.Editing; return new[] { SnipAction.ShowEditor };
            case SnipState.Editing:
                return new[] { SnipAction.AddBox };
            default:
                return None;
        }
    }

    // 一覧に加えるのに失敗したら（E-02）、編集画面を開いたままにする
    public void AddFailed() => State = SnipState.Editing;

    // 画面が想定外に閉じた（Alt＋F4 など）・処理中に例外が起きたときは待機に戻す（verify V1・V2）。
    // 戻さないと以後の起動キーが E-07 として無視され続ける
    public void Reset() => State = SnipState.Idle;
}
