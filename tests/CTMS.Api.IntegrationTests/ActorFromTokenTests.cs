using System.Net;
using System.Net.Http.Json;
using CTMS.Api.Auth;
using CTMS.Api.IntegrationTests.Support;
using CTMS.Application.Translations;

namespace CTMS.Api.IntegrationTests;

/// <summary>
/// A real (non dev-bypass) token wins over a client-supplied actor: the persisted and returned
/// <c>updatedBy</c> is the token's <c>name</c>, not the request body's <c>updatedBy</c>.
/// </summary>
[Collection(IntegrationCollection.Name)]
public sealed class ActorFromTokenTests(MongoFixture mongo) : IntegrationTest(mongo)
{
    [Fact]
    public async Task Upsert_records_the_token_name_not_the_body_updatedBy()
    {
        using var admin = Factory.ClientAs(AuthRoles.Admin);
        await admin.CreateLanguageAsync("en-GB", "English");
        var app = await admin.CreateApplicationAsync(
            code: ApiHelpers.UniqueName("actor"), enabledLanguageCodes: ["en-GB"]);
        var key = await admin.CreateKeyAsync(app.Code, "actor.key");

        using var translator = Factory.ClientAsActor("translator-alice", AuthRoles.Translator);

        using var putResponse = await translator.PutAsJsonAsync(
            $"/api/applications/{app.Code}/keys/{key.Id}/strings/en-GB",
            new UpsertTranslationStringRequest("hello", UpdatedBy: "someone-else"));

        Assert.Equal(HttpStatusCode.Created, putResponse.StatusCode);
        var created = (await putResponse.Content.ReadFromJsonAsync<TranslationStringDto>())!;
        Assert.Equal("translator-alice", created.UpdatedBy);

        var fetched = (await translator.GetFromJsonAsync<TranslationStringDto>(
            $"/api/applications/{app.Code}/keys/{key.Id}/strings/en-GB"))!;
        Assert.Equal("translator-alice", fetched.UpdatedBy);
    }
}
