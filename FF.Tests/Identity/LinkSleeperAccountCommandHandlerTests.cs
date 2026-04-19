using FF.Application.Identity.Commands.LinkSleeperAccount;
using FF.Application.Identity.Interfaces;
using FF.Application.Interfaces.Services;          // ← add for ISleeperLeagueImportService
using FF.Application.Features.Leagues.Commands.ImportLeague;
using Microsoft.Extensions.Logging;                // ← add for ILogger
using NSubstitute;

namespace FF.Tests.Identity
{
    public class LinkSleeperAccountCommandHandlerTests
    {
        private readonly ISleeperIdentityService _sleeperIdentityService;
        private readonly IUserRepository _userRepository;
        private readonly ILeagueMembershipRepository _leagueMembershipRepository;
        private readonly ISleeperLeagueImportService _leagueImportService;   // ← add
        private readonly ILogger<LinkSleeperAccountCommandHandler> _logger;   // ← add
        private readonly LinkSleeperAccountCommandHandler _handler;

        public LinkSleeperAccountCommandHandlerTests()
        {
            _sleeperIdentityService = Substitute.For<ISleeperIdentityService>();
            _userRepository = Substitute.For<IUserRepository>();
            _leagueMembershipRepository = Substitute.For<ILeagueMembershipRepository>();
            _leagueImportService = Substitute.For<ISleeperLeagueImportService>();  // ← add
            _logger = Substitute.For<ILogger<LinkSleeperAccountCommandHandler>>(); // ← add

            // Default — return empty leagues so existing tests aren't affected
            _sleeperIdentityService
                .GetUserLeaguesAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(new List<SleeperUserLeague>().AsReadOnly());

            // Default — import succeeds silently
            _leagueImportService
                .ImportLeagueAsync(Arg.Any<string>(), Arg.Any<CancellationToken>())
                .Returns(Task.FromResult(new ImportLeagueResult(
                    LeagueName: "Test League",
                    LeagueId: "test-id",
                    RostersImported: 0,
                    PlayersImported: 0,
                    TransactionsImported: 0,
                    WasNewLeague: true)));

            _handler = new LinkSleeperAccountCommandHandler(
                _sleeperIdentityService,
                _userRepository,
                _leagueMembershipRepository,
                _leagueImportService,   // ← add
                _logger);               // ← add
        }
    }
}