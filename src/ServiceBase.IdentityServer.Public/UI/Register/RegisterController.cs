﻿using IdentityServer4.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using ServiceBase.IdentityServer.Config;
using ServiceBase.IdentityServer.Crypto;
using ServiceBase.IdentityServer.Extensions;
using ServiceBase.IdentityServer.Models;
using ServiceBase.IdentityServer.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using ServiceBase.IdentityServer.Events;
using ServiceBase.Notification.Email;

namespace ServiceBase.IdentityServer.Public.UI.Login
{
    public class RegisterController : Controller
    {
        private readonly ApplicationOptions _applicationOptions;
        private readonly ILogger<RegisterController> _logger;
        private readonly IIdentityServerInteractionService _interaction;
        private readonly IUserAccountStore _userAccountStore;
        private readonly ICrypto _crypto;
        private readonly IEmailService _emailService;
        private readonly IEventService _eventService; 

        public RegisterController(
            IOptions<ApplicationOptions> applicationOptions,
            ILogger<RegisterController> logger,
            IIdentityServerInteractionService interaction,
            IUserAccountStore userAccountStore,
            ICrypto crypto,
            IEmailService emailService,
            IEventService eventService)
        {
            _applicationOptions = applicationOptions.Value;
            _logger = logger;
            _interaction = interaction;
            _userAccountStore = userAccountStore;
            _emailService = emailService;
            _crypto = crypto;
            _eventService = eventService; 
        }

        [HttpGet("register", Name = "Register")]
        public async Task<IActionResult> Index(string returnUrl)
        {
            var vm = new RegisterViewModel();

            if (!String.IsNullOrWhiteSpace(returnUrl))
            {
                var request = await _interaction.GetAuthorizationContextAsync(returnUrl);
                if (request != null)
                {
                    vm.Email = request.LoginHint;
                    vm.ReturnUrl = returnUrl;
                }
            }

            return View(vm);
        }

        static readonly string[] UglyBase64 = { "+", "/", "=" };
        protected virtual string StripUglyBase64(string s)
        {
            if (s == null) return s;
            foreach (var ugly in UglyBase64)
            {
                s = s.Replace(ugly, String.Empty);
            }
            return s;
        }

        [HttpPost("register")]
        [ValidateAntiForgeryToken]
        public async Task<IActionResult> Index(RegisterInputModel model)
        {
            if (ModelState.IsValid)
            {
                var email = model.Email.ToLower();

                // Check if user with same email exists
                var userAccount = await _userAccountStore.LoadByEmailWithExternalAsync(email);

                // If user dont exists create a new one 
                if (userAccount == null)
                {
                    var now = DateTime.UtcNow; 

                    // Create new user instance 
                    userAccount = new UserAccount
                    {
                        Email = model.Email,
                        PasswordHash = _crypto.HashPassword(model.Password, _applicationOptions.PasswordHashingIterationCount),
                        PasswordChangedAt = now,
                        FailedLoginCount = 0,
                        IsEmailVerified = false,
                        IsLoginAllowed = _applicationOptions.LoginAfterAccountCreation
                    };
                    
                    #region Send email verification message 

                    userAccount.SetVerification(
                        StripUglyBase64(_crypto.Hash(_crypto.GenerateSalt())),
                        VerificationKeyPurpose.ConfirmAccount,
                        model.ReturnUrl,
                        now); 

                    await _emailService.SendEmailAsync("AccountCreatedEvent", userAccount.Email, new
                    {
                        Token = userAccount.VerificationKey 
                    }); 

                    #endregion 
                    
                    // Save user to data store 
                    userAccount = await _userAccountStore.WriteAsync(userAccount);

                    // Emit event 
                    // _eventService.RaiseSuccessfulUserRegisteredEventAsync()

                    if (_applicationOptions.LoginAfterAccountCreation)
                    {
                        await HttpContext.Authentication.IssueCookie(userAccount, "idsvr", "password");

                        if (model.ReturnUrl != null && _interaction.IsValidReturnUrl(model.ReturnUrl))
                        {
                            return Redirect(model.ReturnUrl);
                        }
                    }
                    else
                    {
                        // Redirect to success page by preserving the email provider name 
                        return Redirect(Url.Action("Success", "Register", new
                        {
                            returnUrl = model.ReturnUrl,
                            provider = userAccount.Email.Split('@').LastOrDefault()
                        }));
                    }
                }
                else if (!userAccount.IsLoginAllowed)
                {
                    if (!userAccount.IsEmailVerified)
                    {
                        ModelState.AddModelError("", "Please confirm your email account");
                    }
                    else
                    {
                        ModelState.AddModelError("", "Your user account has be disabled");
                    }
                }
                else
                {
                    // If user has a password then its a local account 
                    if (!userAccount.HasPassword())
                    {
                        ModelState.AddModelError("", "User already exists");
                    }
                    else
                    {

                    }

                    // Return list of external account providers as hint
                    var vm = new RegisterViewModel(model);
                    vm.HintExternalAccounts = userAccount.Accounts.Select(s => s.Provider).ToArray();
                    return View(vm);
                }
                // As if user wants to use other account instead 

                // if yes, cancel registration and redirect to login 
                // if no ask if he wants to merge accounts 

                // if yes, link account
                // if no create user 
            }

            return View(new RegisterViewModel(model));
        }
        
        [HttpGet("register/success", Name = "RegisterSuccess")]
        public async Task<IActionResult> Success(string returnUrl, string provider)
        {
            // select propper mail provider and render it as button 

            return View();
        }

        [HttpGet("register/confirm/{key}", Name = "RegisterConfirm")]
        public async Task<IActionResult> Confirm(string key)
        {
            // Load token data from database 
            var userAccount = await _userAccountStore.LoadByVerificationKeyAsync(key);

            if (userAccount == null)
            {
                // ERROR
            }

            if (userAccount.VerificationPurpose != (int)VerificationKeyPurpose.ConfirmAccount)
            {
                // ERROR
            }
            
            // TODO: check if user exists
            // TODO: check if token expired 
            
            var returnUrl = userAccount.VerificationStorage;

            userAccount.IsLoginAllowed = true; 
            userAccount.IsEmailVerified = true;
            userAccount.EmailVerifiedAt = DateTime.UtcNow;
            userAccount.ClearVerification(); 

            // Update user account 
            userAccount = await _userAccountStore.UpdateAsync(userAccount);

            // TODO: settings for auto signin after confirmation 
            if (_applicationOptions.LoginAfterAccountConfirmation)
            {
                await HttpContext.Authentication.IssueCookie(userAccount, "idsvr", "password");

                if (returnUrl != null && _interaction.IsValidReturnUrl(returnUrl))
                {
                    return Redirect(returnUrl);
                }
            }

            return Redirect(Url.Action("login", new { ReturnUrl = returnUrl }));
        }

        [HttpGet("register/cancel/{key}", Name = "RegistersCancel")]
        public async Task<IActionResult> Cancel(string key)
        {
            // Load token data from database 
            var userAccount = await _userAccountStore.LoadByVerificationKeyAsync(key);

            if (userAccount == null)
            {
                // ERROR
            }

            if (userAccount.VerificationPurpose != (int)VerificationKeyPurpose.ConfirmAccount)
            {
                // ERROR
            }
            
            if (userAccount.LastLoginAt != null)
            {
                // ERROR
            }

            await _userAccountStore.DeleteByIdAsync(userAccount.Id);

            var returnUrl = userAccount.VerificationStorage;

            return Redirect(Url.Action("login", new { returnUrl = returnUrl }));
        }
    }
}