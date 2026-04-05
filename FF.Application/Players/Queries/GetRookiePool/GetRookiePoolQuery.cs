// FF.Application/Players/Queries/GetRookiePool/GetRookiePoolQuery.cs
using FF.Application.Common.Models;
using FF.SharedKernel.Common;
using MediatR;

namespace FF.Application.Players.Queries.GetRookiePool;

public record GetRookiePoolQuery(string? Position = null) : IRequest<Result<List<RookiePlayerDto>>>;