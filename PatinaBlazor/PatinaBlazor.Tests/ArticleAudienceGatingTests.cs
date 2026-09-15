using System.Security.Claims;
using Microsoft.AspNetCore.Components.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.DependencyInjection;
using PatinaBlazor.Data;
using PatinaBlazor.Services;
using Xunit;

namespace PatinaBlazor.Tests
{
    // Covers the access-control boundary between an Article and who can actually see it -
    // ArticleService.GetVisibleArticlesAsync/GetPublishedArticleAsync (the server-side
    // enforcement - see CLAUDE.md's 2026-09-06 Articles entry: "gates access... enforced
    // server-side, never just hidden client-side") and ArticleAudienceResolver (what audiences
    // a given viewer is allowed to see, the input to that enforcement). Neither had a
    // permanent test before this - only a one-time browser check (raw <script> tag / audience
    // article visibility, verified once then never re-checked).
    [Collection("Database")]
    public class ArticleAudienceGatingTests
    {
        private readonly DatabaseFixture _fixture;

        public ArticleAudienceGatingTests(DatabaseFixture fixture)
        {
            _fixture = fixture;
        }

        [Fact]
        public async Task GetVisibleArticlesAsync_ExcludesDraftAndArchivedRegardlessOfAudience()
        {
            using var scope = _fixture.CreateScope();
            var articleService = scope.ServiceProvider.GetRequiredService<IArticleService>();
            var authorId = await GetAuthorIdAsync(scope);

            var draft = await CreateArticleAsync(articleService, authorId, ArticleStatus.Draft, ArticleAudience.Public);
            var archived = await CreateArticleAsync(articleService, authorId, ArticleStatus.Archived, ArticleAudience.Public);

            var visible = await articleService.GetVisibleArticlesAsync(new[] { ArticleAudience.Public, ArticleAudience.RegisteredUsers, ArticleAudience.StorageCustomers });

            Assert.DoesNotContain(visible, a => a.Id == draft.Id);
            Assert.DoesNotContain(visible, a => a.Id == archived.Id);

            await articleService.DeleteAsync(draft.Id);
            await articleService.DeleteAsync(archived.Id);
        }

        [Fact]
        public async Task GetVisibleArticlesAsync_ExcludesPublishedArticleWhoseAudienceIsntAllowed()
        {
            using var scope = _fixture.CreateScope();
            var articleService = scope.ServiceProvider.GetRequiredService<IArticleService>();
            var authorId = await GetAuthorIdAsync(scope);

            var registeredOnly = await CreateArticleAsync(articleService, authorId, ArticleStatus.Published, ArticleAudience.RegisteredUsers);

            var anonymousView = await articleService.GetVisibleArticlesAsync(new[] { ArticleAudience.Public });
            Assert.DoesNotContain(anonymousView, a => a.Id == registeredOnly.Id);

            var registeredView = await articleService.GetVisibleArticlesAsync(new[] { ArticleAudience.Public, ArticleAudience.RegisteredUsers });
            Assert.Contains(registeredView, a => a.Id == registeredOnly.Id);

            await articleService.DeleteAsync(registeredOnly.Id);
        }

        [Fact]
        public async Task GetPublishedArticleAsync_ReturnsNullForDraftAudienceMismatchAndUnknownId()
        {
            using var scope = _fixture.CreateScope();
            var articleService = scope.ServiceProvider.GetRequiredService<IArticleService>();
            var authorId = await GetAuthorIdAsync(scope);

            var draft = await CreateArticleAsync(articleService, authorId, ArticleStatus.Draft, ArticleAudience.Public);
            var storageOnly = await CreateArticleAsync(articleService, authorId, ArticleStatus.Published, ArticleAudience.StorageCustomers);

            // A draft is never returned, even to an allowed-audience viewer requesting it directly.
            Assert.Null(await articleService.GetPublishedArticleAsync(draft.Id, new[] { ArticleAudience.Public, ArticleAudience.RegisteredUsers, ArticleAudience.StorageCustomers }));

            // Published, but the viewer's allowed audiences don't include StorageCustomers.
            Assert.Null(await articleService.GetPublishedArticleAsync(storageOnly.Id, new[] { ArticleAudience.Public, ArticleAudience.RegisteredUsers }));

            // Same article, viewer IS allowed StorageCustomers - must be returned.
            Assert.NotNull(await articleService.GetPublishedArticleAsync(storageOnly.Id, new[] { ArticleAudience.Public, ArticleAudience.StorageCustomers }));

            // An id that doesn't exist at all - null, not an exception. Deliberately the same
            // outcome as "exists but not allowed" (see GetPublishedArticleAsync's own doc
            // comment) - a real 404 vs. an access-denied response would leak whether an
            // audience-restricted article exists.
            Assert.Null(await articleService.GetPublishedArticleAsync(Guid.NewGuid(), new[] { ArticleAudience.Public, ArticleAudience.RegisteredUsers, ArticleAudience.StorageCustomers }));

            await articleService.DeleteAsync(draft.Id);
            await articleService.DeleteAsync(storageOnly.Id);
        }

        [Fact]
        public async Task GetAllowedAudiences_AnonymousUser_OnlyPublic()
        {
            var allowed = await ArticleAudienceResolver.GetAllowedAudiencesAsync(new FakeAuthenticationStateProvider(authenticated: false));

            Assert.Equal(new[] { ArticleAudience.Public }, allowed);
        }

        [Fact]
        public async Task GetAllowedAudiences_AuthenticatedWithNoSpecialRole_PublicAndRegisteredUsers()
        {
            var allowed = await ArticleAudienceResolver.GetAllowedAudiencesAsync(new FakeAuthenticationStateProvider(authenticated: true));

            Assert.Equal(new[] { ArticleAudience.Public, ArticleAudience.RegisteredUsers }, allowed);
        }

        [Fact]
        public async Task GetAllowedAudiences_StorageCustomerRole_AlsoIncludesStorageCustomers()
        {
            var allowed = await ArticleAudienceResolver.GetAllowedAudiencesAsync(
                new FakeAuthenticationStateProvider(authenticated: true, roles: new[] { StorageService.StorageCustomerRoleName }));

            Assert.Equal(new[] { ArticleAudience.Public, ArticleAudience.RegisteredUsers, ArticleAudience.StorageCustomers }, allowed);
        }

        private static async Task<string> GetAuthorIdAsync(IServiceScope scope)
        {
            var userManager = scope.ServiceProvider.GetRequiredService<UserManager<ApplicationUser>>();
            var author = await userManager.FindByEmailAsync(DevTestAccounts.AutomationEmail);
            Assert.NotNull(author);
            return author!.Id;
        }

        private static async Task<Article> CreateArticleAsync(IArticleService articleService, string authorId, ArticleStatus status, ArticleAudience audience)
        {
            return await articleService.CreateAsync(new Article
            {
                Title = $"Audience Gating Test {Guid.NewGuid():N}",
                Body = "test body",
                AuthorUserId = authorId,
                Status = status,
                Audience = audience
            }, authorId);
        }

        private sealed class FakeAuthenticationStateProvider : AuthenticationStateProvider
        {
            private readonly bool _authenticated;
            private readonly string[] _roles;

            public FakeAuthenticationStateProvider(bool authenticated, string[]? roles = null)
            {
                _authenticated = authenticated;
                _roles = roles ?? Array.Empty<string>();
            }

            public override Task<AuthenticationState> GetAuthenticationStateAsync()
            {
                var identity = _authenticated
                    ? new ClaimsIdentity(new[] { new Claim(ClaimTypes.Name, "test-user") }
                        .Concat(_roles.Select(r => new Claim(ClaimTypes.Role, r))), authenticationType: "Test")
                    : new ClaimsIdentity();

                return Task.FromResult(new AuthenticationState(new ClaimsPrincipal(identity)));
            }
        }
    }
}
