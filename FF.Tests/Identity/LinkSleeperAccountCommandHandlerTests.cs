using FF.Application.Identity.Commands.LinkSleeperAccount;
using FF.Application.Identity.Interfaces;
using NSubstitute;

namespace FF.Tests.Identity
{
    public class LinkSleeperAccountCommandHandlerTests
    {
        private readonly ISleeperIdentityService _sleeperIdentityService;
        private readonly IUserRepository _userRepository;
        private readonly ILeagueMembershipRepository _leagueMembershipRepository;
        private readonly LinkSleeperAccountCommandHandler _handler;

        public LinkSleeperAccountCommandHandlerTests()
        {
            _sleeperIdentityService = Substitute.For<ISleeperIdentityService>();
            _userRepository = Substitute.For<IUserRepository>();
            _leagueMembershipRepository = Substitute.For<ILeagueMembershipRepository>();

            // Default — return empty leagues so existing tests aren't affected
            _sleeperIdentityService
                .GetUserLeaguesAsync(Arg.Any<string>(), Arg.Any<int>(), Arg.Any<CancellationToken>())
                .Returns(new List<SleeperUserLeague>().AsReadOnly());

            _handler = new LinkSleeperAccountCommandHandler(
                _sleeperIdentityService,
                _userRepository,
                _leagueMembershipRepository);
        }

        // ... rest of tests unchanged
    }
}