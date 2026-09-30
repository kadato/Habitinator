namespace App.Shared.RCL.Services.Board.Local;

public sealed record BoardLocalState(
    List<BoardLocalRow> Items,
    List<BoardOutboxEntry> Outbox,
    BoardStoreMeta Meta)
{
    public BoardLocalState()
        : this([], [], new BoardStoreMeta())
    {
    }
}
