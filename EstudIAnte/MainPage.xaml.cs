namespace EstudIAnte
{
    public partial class MainPage : ContentPage
    {
        public MainPage()
        {
            InitializeComponent();
        }

        private void OnNavChanged(object? sender, CheckedChangedEventArgs e)
        {
            if (sender is not RadioButton radioButton || !radioButton.IsChecked)
                return;

            VInicio.IsVisible = ReferenceEquals(radioButton, N1);
            VChat.IsVisible = ReferenceEquals(radioButton, N2);
            VQuiz.IsVisible = ReferenceEquals(radioButton, N3);
            VDocs.IsVisible = ReferenceEquals(radioButton, N4);
        }
    }
}
