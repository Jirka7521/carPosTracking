using CarPosAPI.Options;
using Microsoft.Extensions.Configuration;

namespace CarPosAPI.Tests;

/// <summary>
/// Guards the sign-up default. A deployment that forgets the <c>Registration</c>
/// section must come up invite-only, not open: open sign-up is the door strangers
/// used to walk through, and it must take a deliberate setting to reopen it.
/// </summary>
public sealed class RegistrationOptionsTests
{
    [Fact]
    public void DefaultsToInviteOnly()
    {
        Assert.Equal(RegistrationMode.InviteOnly, new RegistrationOptions().Mode);
    }

    [Theory]
    [InlineData("InviteOnly", RegistrationMode.InviteOnly)]
    [InlineData("Open", RegistrationMode.Open)]
    [InlineData("closed", RegistrationMode.Closed)]
    public void BindsTheModeByName(string configured, RegistrationMode expected)
    {
        // Registration__Mode=Open in a compose file arrives as a string; it has to land
        // on the enum value it names, case aside.
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Registration:Mode"] = configured })
            .Build();

        RegistrationOptions options = new RegistrationOptions();
        configuration.GetSection(RegistrationOptions.SectionName).Bind(options);

        Assert.Equal(expected, options.Mode);
    }
}
