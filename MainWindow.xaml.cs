using Microsoft.Extensions.DependencyInjection;
using System;
using System.Windows;
using WpfBlazorSearchTool.Services;

namespace WpfBlazorSearchTool
{
    public partial class MainWindow : Window
    {
        public MainWindow()
        {
            InitializeComponent();

            var serviceCollection = new ServiceCollection();
            serviceCollection.AddWpfBlazorWebView();

            // VVVV --- ADD THIS LINE TO ENABLE DEV TOOLS --- VVVV
            serviceCollection.AddBlazorWebViewDeveloperTools();
            // ^^^^ --- END OF NEW LINE --- ^^^^

            // Register your services here
            serviceCollection.AddSingleton<IFileSaveService, WpfFileSaveService>();
            serviceCollection.AddSingleton<SearchService>();
            serviceCollection.AddSingleton<DatabaseSearchService>();
            
            Resources.Add("services", serviceCollection.BuildServiceProvider());
        }
    }
}