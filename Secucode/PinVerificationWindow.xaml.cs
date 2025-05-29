using System;
using System.Windows;
using BCrypt.Net;

namespace Secucode
{
    public partial class PinVerificationWindow : Window
    {
        private string hashedPin;
        public bool IsPinVerified { get; private set; }
        public PinVerificationWindow(string hashedPin)
        {
            InitializeComponent();
            this.hashedPin = hashedPin; // Store hashed PIN
            IsPinVerified = false; // Initial value
        }

        // Event handler for Verify button click
        private void VerifyButton_Click(object sender, RoutedEventArgs e)
        {
            string enteredPin = PinInputBox.Password;

            // Verify entered PIN with hashed PIN
            if (BCrypt.Net.BCrypt.Verify(enteredPin, hashedPin))
            {
                IsPinVerified = true;
                MessageBox.Show("PIN verified successfully!", "Success", MessageBoxButton.OK, MessageBoxImage.Information);
                this.DialogResult = true; // Close the dialog with success
            }
            else
            {
                MessageBox.Show("Incorrect PIN. Please try again.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
                IsPinVerified = false;
            }
        }

        // Event handler for Cancel button click
        private void CancelButton_Click(object sender, RoutedEventArgs e)
        {
            this.DialogResult = false; // Close the dialog without verification
        }
    }
}
