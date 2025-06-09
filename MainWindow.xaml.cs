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

            // Register your services here
            serviceCollection.AddSingleton<IFileSaveService, WpfFileSaveService>();
            serviceCollection.AddSingleton<SearchService>();

            Resources.Add("services", serviceCollection.BuildServiceProvider());
        }
    }
}