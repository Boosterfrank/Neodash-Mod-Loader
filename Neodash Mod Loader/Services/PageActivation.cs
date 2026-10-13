using Microsoft.Extensions.DependencyInjection;
using Microsoft.UI.Xaml.Controls;
using Neodash_Mod_Loader.Views;

namespace Neodash_Mod_Loader.Services;

// Frame constructs XAML pages through their public parameterless constructors.
// Keep the scoped container lookup here, rather than exposing it to the pages.
public sealed class PageActivation(IServiceProvider services) : IPageActivation
{
    public void Activate(Page page)
    {
        switch (page)
        {
            case HomePage home:
                home.Activate(services.GetRequiredService<HomePageController>());
                break;
            case ModLibraryPage library:
                library.Activate(services.GetRequiredService<ModLibraryController>());
                break;
            case SettingsPage settings:
                settings.Activate(services.GetRequiredService<SettingsPageController>());
                break;
            case OnboardingPage onboarding:
                onboarding.Activate(services.GetRequiredService<OnboardingController>());
                break;
            case ShellPage shell:
                shell.Activate(services.GetRequiredService<ShellController>());
                break;
            default:
                throw new InvalidOperationException($"No controller is registered for {page.GetType().Name}.");
        }
    }
}
