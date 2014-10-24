﻿/*
Copyright (c) 2014, Lars Brubaker & Kevin Pope
All rights reserved.

Redistribution and use in source and binary forms, with or without
modification, are permitted provided that the following conditions are met: 

1. Redistributions of source code must retain the above copyright notice, this
   list of conditions and the following disclaimer. 
2. Redistributions in binary form must reproduce the above copyright notice,
   this list of conditions and the following disclaimer in the documentation
   and/or other materials provided with the distribution. 

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND
ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED
WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE
DISCLAIMED. IN NO EVENT SHALL THE COPYRIGHT OWNER OR CONTRIBUTORS BE LIABLE FOR
ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES
(INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES;
LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND
ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT
(INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS
SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.

The views and conclusions contained in the software and documentation are those
of the authors and should not be interpreted as representing official policies, 
either expressed or implied, of the FreeBSD Project.
*/

using System;
using System.Collections.Generic;
using MatterHackers.Agg;
using MatterHackers.Agg.UI;
using MatterHackers.MatterControl.FieldValidation;
using MatterHackers.MatterControl.VersionManagement;
using MatterHackers.VectorMath;
using MatterHackers.Localizations;

namespace MatterHackers.MatterControl.Plugins.PrintNotifications
{
    public class FormField
    {
        public delegate ValidationStatus ValidationHandler(string valueToValidate);
        public MHTextEditWidget FieldEditWidget { get; set; }
        public TextWidget FieldErrorMessageWidget { get; set; }
        ValidationHandler[] FieldValidationHandlers { get; set; }

        public FormField(MHTextEditWidget textEditWidget, TextWidget errorMessageWidget, ValidationHandler[] validationHandlers)
        {
            this.FieldEditWidget = textEditWidget;
            this.FieldErrorMessageWidget = errorMessageWidget;
            this.FieldValidationHandlers = validationHandlers;
        }

        public bool Validate()
        {
            bool fieldIsValid = true;
            foreach (ValidationHandler validationHandler in FieldValidationHandlers)
            {
                if (fieldIsValid)
                {
                    ValidationStatus validationStatus = validationHandler(this.FieldEditWidget.Text);
                    if (!validationStatus.IsValid)
                    {
                        fieldIsValid = false;
                        FieldErrorMessageWidget.Text = validationStatus.ErrorMessage;
                        FieldErrorMessageWidget.Visible = true;
                    }
                }
            }
            return fieldIsValid;
        }
    }
    
    public class NotificationFormWidget : GuiWidget
    {
        protected TextImageButtonFactory textImageButtonFactory = new TextImageButtonFactory();
        Button saveButton;
        Button cancelButton;
        Button doneButton;
        FlowLayoutWidget formContainer;
        FlowLayoutWidget messageContainer;
        CheckBox notifySendTextCheckbox;
        CheckBox notifyPlaySoundCheckbox;
        CheckBox notifySendEmailCheckbox;
        GuiWidget phoneNumberLabel;
        GuiWidget phoneNumberHelperLabel;

        FlowLayoutWidget phoneNumberContainer;
        FlowLayoutWidget emailAddressContainer;

        GuiWidget emailAddressLabel;
        GuiWidget emailAddressHelperLabel;
        MHTextEditWidget emailAddressInput;
        TextWidget emailAddressErrorMessage;

        TextWidget submissionStatus;
        GuiWidget centerContainer;

        MHTextEditWidget phoneNumberInput;
        TextWidget phoneNumberErrorMessage;

        public NotificationFormWidget()
        {
            SetButtonAttributes();
            AnchorAll();

            cancelButton = textImageButtonFactory.Generate("Cancel".Localize());
            saveButton = textImageButtonFactory.Generate("Save".Localize());
            doneButton = textImageButtonFactory.Generate("Done".Localize());
            doneButton.Visible = false;

            DoLayout();
            AddButtonHandlers();
        }

        private GuiWidget LabelGenerator(string labelText, int fontSize = 12, int height = 28)
        {
            GuiWidget labelContainer = new GuiWidget();
            labelContainer.HAnchor = HAnchor.ParentLeftRight;
			labelContainer.Height = height * TextWidget.GlobalPointSizeScaleRatio;

            TextWidget formLabel = new TextWidget(labelText, pointSize: fontSize);
            formLabel.TextColor = ActiveTheme.Instance.PrimaryTextColor;
            formLabel.VAnchor = VAnchor.ParentBottom;
            formLabel.HAnchor = HAnchor.ParentLeft;
            formLabel.Margin = new BorderDouble(bottom: 2);

            labelContainer.AddChild(formLabel);

            return labelContainer;
        }

        private TextWidget ErrorMessageGenerator()
        {
            TextWidget formLabel = new TextWidget("", pointSize:11);
            formLabel.AutoExpandBoundsToText = true;
            formLabel.Margin = new BorderDouble(0, 5);
            formLabel.TextColor = RGBA_Bytes.Red;            
            formLabel.HAnchor = HAnchor.ParentLeft;
            formLabel.Visible = false;            

            return formLabel;
        }

        private void DoLayout()
        {
            FlowLayoutWidget mainContainer = new FlowLayoutWidget(FlowDirection.TopToBottom);
            mainContainer.AnchorAll();

            FlowLayoutWidget labelContainer = new FlowLayoutWidget();
            labelContainer.HAnchor = HAnchor.ParentLeftRight;

            TextWidget formLabel = new TextWidget("After a Print is Finished:".Localize(), pointSize: 16);
            formLabel.TextColor = ActiveTheme.Instance.PrimaryTextColor;
            formLabel.VAnchor = VAnchor.ParentCenter;
            formLabel.Margin = new BorderDouble(10, 0,10, 12);
            labelContainer.AddChild(formLabel);
            mainContainer.AddChild(labelContainer);

            centerContainer = new GuiWidget();
            centerContainer.AnchorAll();
            centerContainer.Padding = new BorderDouble(10);

            messageContainer = new FlowLayoutWidget(FlowDirection.TopToBottom);
            messageContainer.AnchorAll();
            messageContainer.Visible = false;
            messageContainer.BackgroundColor = ActiveTheme.Instance.SecondaryBackgroundColor;
            messageContainer.Padding = new BorderDouble(10);
            
            submissionStatus = new TextWidget("Saving your settings...".Localize(), pointSize: 13);
            submissionStatus.AutoExpandBoundsToText = true;
            submissionStatus.Margin = new BorderDouble(0, 5);
            submissionStatus.TextColor = ActiveTheme.Instance.PrimaryTextColor;
            submissionStatus.HAnchor = HAnchor.ParentLeft;

            messageContainer.AddChild(submissionStatus);

            formContainer = new FlowLayoutWidget(FlowDirection.TopToBottom);
            formContainer.AnchorAll();
            formContainer.BackgroundColor = ActiveTheme.Instance.SecondaryBackgroundColor;
            formContainer.Padding = new BorderDouble(10);
            {
                FlowLayoutWidget smsLabelContainer = new FlowLayoutWidget(FlowDirection.LeftToRight);
                smsLabelContainer.Margin = new BorderDouble(0, 2, 0, 4);
                smsLabelContainer.HAnchor |= Agg.UI.HAnchor.ParentLeft;
                
                //Add sms notification option
                notifySendTextCheckbox = new CheckBox("Send an SMS notification".Localize());
                notifySendTextCheckbox.Margin = new BorderDouble(0);
                notifySendTextCheckbox.VAnchor = Agg.UI.VAnchor.ParentBottom;
                notifySendTextCheckbox.TextColor = ActiveTheme.Instance.PrimaryTextColor;
                notifySendTextCheckbox.Cursor = Cursors.Hand;
                notifySendTextCheckbox.Checked = (UserSettings.Instance.get("AfterPrintFinishedSendTextMessage") == "true");
                notifySendTextCheckbox.CheckedStateChanged += new CheckBox.CheckedStateChangedEventHandler(OnSendTextChanged);

                TextWidget experimentalLabel = new TextWidget("Experimental".Localize(), pointSize: 10);
                experimentalLabel.TextColor = ActiveTheme.Instance.SecondaryAccentColor;
                experimentalLabel.VAnchor = Agg.UI.VAnchor.ParentBottom;
                experimentalLabel.Margin = new BorderDouble(left:10);

                smsLabelContainer.AddChild(notifySendTextCheckbox);
                smsLabelContainer.AddChild(experimentalLabel);

                formContainer.AddChild(smsLabelContainer);
                formContainer.AddChild(LabelGenerator("Have MatterControl send you a text message after your print is finished".Localize(), 9, 14));

                phoneNumberContainer = new FlowLayoutWidget(FlowDirection.TopToBottom);
                phoneNumberContainer.HAnchor = Agg.UI.HAnchor.ParentLeftRight;

                phoneNumberLabel = LabelGenerator("Your Phone Number*".Localize());
                phoneNumberHelperLabel = LabelGenerator("A U.S. or Canadian mobile phone number".Localize(), 9, 14);
                

                phoneNumberContainer.AddChild(phoneNumberLabel);
                phoneNumberContainer.AddChild(phoneNumberHelperLabel);

                phoneNumberInput = new MHTextEditWidget();
                phoneNumberInput.HAnchor = HAnchor.ParentLeftRight;

                string phoneNumber = UserSettings.Instance.get("NotificationPhoneNumber");
                if (phoneNumber != null)
                {
                    phoneNumberInput.Text = phoneNumber;
                }

                phoneNumberContainer.AddChild(phoneNumberInput);

                phoneNumberErrorMessage = ErrorMessageGenerator();
                phoneNumberContainer.AddChild(phoneNumberErrorMessage);

                formContainer.AddChild(phoneNumberContainer);
            }

            {
                //Add email notification option
                notifySendEmailCheckbox = new CheckBox("Send an email notification".Localize());
                notifySendEmailCheckbox.Margin = new BorderDouble(0, 2, 0, 16);
                notifySendEmailCheckbox.HAnchor = Agg.UI.HAnchor.ParentLeft;
                notifySendEmailCheckbox.TextColor = ActiveTheme.Instance.PrimaryTextColor;
                notifySendEmailCheckbox.Cursor = Cursors.Hand;
                notifySendEmailCheckbox.Checked = (UserSettings.Instance.get("AfterPrintFinishedSendEmail") == "true");
                notifySendEmailCheckbox.CheckedStateChanged += new CheckBox.CheckedStateChangedEventHandler(OnSendEmailChanged);

                formContainer.AddChild(notifySendEmailCheckbox);
                formContainer.AddChild(LabelGenerator("Have MatterControl send you an email message after your print is finished".Localize(), 9, 14));

                emailAddressContainer = new FlowLayoutWidget(FlowDirection.TopToBottom);
                emailAddressContainer.HAnchor = Agg.UI.HAnchor.ParentLeftRight;

                emailAddressLabel = LabelGenerator("Your Email Address*".Localize());

                emailAddressHelperLabel = LabelGenerator("A valid email address".Localize(), 9, 14);

                emailAddressContainer.AddChild(emailAddressLabel);
                emailAddressContainer.AddChild(emailAddressHelperLabel);

                emailAddressInput = new MHTextEditWidget();
                emailAddressInput.HAnchor = HAnchor.ParentLeftRight;

                string emailAddress = UserSettings.Instance.get("NotificationEmailAddress");
                if (emailAddress != null)
                {
                    emailAddressInput.Text = emailAddress;
                }

                emailAddressContainer.AddChild(emailAddressInput);

                emailAddressErrorMessage = ErrorMessageGenerator();
                emailAddressContainer.AddChild(emailAddressErrorMessage);

                formContainer.AddChild(emailAddressContainer);
            }

            notifyPlaySoundCheckbox = new CheckBox("Play a Sound".Localize());
            notifyPlaySoundCheckbox.Margin = new BorderDouble(0, 2, 0, 16);
            notifyPlaySoundCheckbox.HAnchor = Agg.UI.HAnchor.ParentLeft;
            notifyPlaySoundCheckbox.TextColor = ActiveTheme.Instance.PrimaryTextColor;
            notifyPlaySoundCheckbox.Checked = (UserSettings.Instance.get("AfterPrintFinishedPlaySound") == "true");
            notifyPlaySoundCheckbox.Cursor = Cursors.Hand;

            formContainer.AddChild(notifyPlaySoundCheckbox);
            formContainer.AddChild(LabelGenerator("Play a sound after your print is finished".Localize(), 9, 14));

            centerContainer.AddChild(formContainer);

            mainContainer.AddChild(centerContainer);
            
            FlowLayoutWidget buttonBottomPanel = GetButtonButtonPanel();
            buttonBottomPanel.AddChild(saveButton);
            buttonBottomPanel.AddChild(cancelButton);
            buttonBottomPanel.AddChild(doneButton);

            mainContainer.AddChild(buttonBottomPanel);

            this.AddChild(mainContainer);

            SetVisibleStates();
        }

        void OnSendTextChanged(object sender, EventArgs e)
        {
            SetVisibleStates();
        }

        void OnSendEmailChanged(object sender, EventArgs e)
        {
            SetVisibleStates();
        }

        void SetVisibleStates()
        {
            phoneNumberContainer.Visible =  notifySendTextCheckbox.Checked;
            emailAddressContainer.Visible = notifySendEmailCheckbox.Checked;
        }

        private bool ValidateContactForm()
        {
            ValidationMethods validationMethods = new ValidationMethods();
            
            List<FormField> formFields = new List<FormField>{};
            FormField.ValidationHandler[] stringValidationHandlers = new FormField.ValidationHandler[] { validationMethods.StringIsNotEmpty };
            FormField.ValidationHandler[] emailValidationHandlers = new FormField.ValidationHandler[] { validationMethods.StringIsNotEmpty, validationMethods.StringLooksLikeEmail };
            FormField.ValidationHandler[] phoneValidationHandlers = new FormField.ValidationHandler[] { validationMethods.StringIsNotEmpty, validationMethods.StringLooksLikePhoneNumber };

            formFields.Add(new FormField(phoneNumberInput, phoneNumberErrorMessage, phoneValidationHandlers));
            formFields.Add(new FormField(emailAddressInput, emailAddressErrorMessage, emailValidationHandlers));

            bool formIsValid = true;
            foreach (FormField formField in formFields)
            {
                //Only validate field if visible
                if (formField.FieldEditWidget.Parent.Visible == true)
                {
                    formField.FieldErrorMessageWidget.Visible = false;
                    bool fieldIsValid = formField.Validate();
                    if (!fieldIsValid)
                    {
                        formIsValid = false;
                    }
                }
            }
            return formIsValid;
        }

        private void AddButtonHandlers()
        {
            cancelButton.Click += (sender, e) => {
                UiThread.RunOnIdle((state) =>
                {
                    Close(); 
                });             
            };
            doneButton.Click += (sender, e) => {
                UiThread.RunOnIdle((state) =>
                {
                    Close();
                }); 
            };
            saveButton.Click += new EventHandler(SubmitContactForm);
        }

        void SubmitContactForm(object sender, EventArgs mouseEvent)
        {
            UiThread.RunOnIdle(DoSubmitContactForm);
        }

        void DoSubmitContactForm(object state)
        {
            if (ValidateContactForm())
            {
                if (notifySendTextCheckbox.Checked)
                {
                    UserSettings.Instance.set("AfterPrintFinishedSendTextMessage", "true");
                    UserSettings.Instance.set("NotificationPhoneNumber", phoneNumberInput.Text);
                }
                else
                {
                    UserSettings.Instance.set("AfterPrintFinishedSendTextMessage", "false");
                }

                if (notifySendEmailCheckbox.Checked)
                {
                    UserSettings.Instance.set("AfterPrintFinishedSendEmail", "true");
                    UserSettings.Instance.set("NotificationEmailAddress", emailAddressInput.Text);
                }
                else
                {
                    UserSettings.Instance.set("AfterPrintFinishedSendEmail", "false");
                }

                if (notifyPlaySoundCheckbox.Checked)
                {
                    UserSettings.Instance.set("AfterPrintFinishedPlaySound", "true");
                }
                else
                {
                    UserSettings.Instance.set("AfterPrintFinishedPlaySound", "false");
                }

                if (ApplicationSettings.Instance.get("ClientToken") == null)
                {
                    RequestClientToken request = new RequestClientToken();
                    //request.RequestSucceeded += new EventHandler(onClientTokenRequestSucceeded);
                    request.Request();
                }

                Close();              
            }
        }

        private FlowLayoutWidget GetButtonButtonPanel()
        {
            FlowLayoutWidget buttonBottomPanel = new FlowLayoutWidget(FlowDirection.LeftToRight);
            buttonBottomPanel.HAnchor = HAnchor.ParentLeftRight;
            buttonBottomPanel.Padding = new BorderDouble(10, 3);
            buttonBottomPanel.BackgroundColor = ActiveTheme.Instance.PrimaryBackgroundColor;
            return buttonBottomPanel;
        }

        private void SetButtonAttributes()
        {
            textImageButtonFactory.normalTextColor = ActiveTheme.Instance.PrimaryTextColor;
            textImageButtonFactory.hoverTextColor = ActiveTheme.Instance.PrimaryTextColor;
            textImageButtonFactory.disabledTextColor = ActiveTheme.Instance.PrimaryTextColor;
            textImageButtonFactory.pressedTextColor = ActiveTheme.Instance.PrimaryTextColor;
        }
    }

    public class NotificationFormWindow : SystemWindow
    {
        static NotificationFormWindow contactFormWindow;
        static bool contactFormIsOpen;

        static public void Open()
        {
            if (!contactFormIsOpen)
            {
                contactFormWindow = new NotificationFormWindow();
                contactFormIsOpen = true;
                contactFormWindow.Closed += (sender, e) => { contactFormIsOpen = false; };                
            }
            else
            {
                if (contactFormWindow != null)
                {
                    contactFormWindow.BringToFront();
                }
            }
        }

        NotificationFormWidget contactFormWidget;

        private NotificationFormWindow()
            : base(500, 550)
        {
            Title = "MatterControl: Notification Options";

            BackgroundColor = ActiveTheme.Instance.PrimaryBackgroundColor;

            contactFormWidget = new NotificationFormWidget();

            AddChild(contactFormWidget);
            AddHandlers();

            ShowAsSystemWindow();
            MinimumSize = new Vector2(500, 550);
        }

        event EventHandler unregisterEvents;
        public override void OnClosed(EventArgs e)
        {
            if (unregisterEvents != null)
            {
                unregisterEvents(this, null);
            }
            base.OnClosed(e);
        }

        private void AddHandlers()
        {
            ActiveTheme.Instance.ThemeChanged.RegisterEvent(ThemeChanged, ref unregisterEvents);
            contactFormWidget.Closed += (sender, e) => { Close(); };
        }

        public void ThemeChanged(object sender, EventArgs e)
        {
            this.Invalidate();
        }
    }
}

