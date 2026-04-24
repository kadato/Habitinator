using App.Shared.RCL.Models;

namespace App.Web.Services;

public sealed record ItemTitleRequest(string Title);

public sealed record BoardSectionRequest(BoardSection Section);
