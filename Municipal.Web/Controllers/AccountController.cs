using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using Municipal.Web.Models;

public class AccountController : Controller
{
    private readonly SignInManager<IdentityUser> _signIn;
    private readonly UserManager<IdentityUser> _users;
    private readonly ILogger<AccountController> _logger;

    public AccountController(
        SignInManager<IdentityUser> signIn,
        UserManager<IdentityUser> users,
        ILogger<AccountController> logger)
    {
        _signIn = signIn;
        _users = users;
        _logger = logger;
    }

    [HttpGet]
    public IActionResult Login() => View(new LoginForm());

    [HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Login(LoginForm form)
    {
        if (!ModelState.IsValid) return View(form);

        var email = form.Email.Trim();
        try
        {
            var user = await _users.FindByEmailAsync(email);
            // Generic failure message to avoid user enumeration
            const string genericError = "Login unsuccessful. Check your credentials or try again later.";

            if (user == null)
            {
                _logger.LogWarning("Login attempt with unknown email: {Email}", email);
                ModelState.AddModelError(string.Empty, genericError);
                return View(form);
            }

            if (!await _users.IsInRoleAsync(user, "Administrator"))
            {
                _logger.LogWarning("Unauthorized role login attempt for {Email}", email);
                ModelState.AddModelError(string.Empty, genericError);
                return View(form);
            }

            if (!await _users.IsEmailConfirmedAsync(user))
            {
                _logger.LogInformation("Unconfirmed email login attempt for {Email}", email);
                // Use the generic error to avoid confirming the account exists.
                ModelState.AddModelError(string.Empty, genericError);
                return View(form);
            }

            var result = await _signIn.CheckPasswordSignInAsync(user, form.Password, lockoutOnFailure: true);
            if (result.Succeeded)
            {
                await _signIn.SignInAsync(user, isPersistent: false);
                return RedirectToAction("Index", "Admin");
            }

            if (result.IsLockedOut)
            {
                _logger.LogWarning("Locked out user attempted login: {Email}", email);
                ModelState.AddModelError(string.Empty, "Your account is temporarily locked.");
                return View(form);
            }

            _logger.LogInformation("Invalid credentials for {Email}", email);
            ModelState.AddModelError(string.Empty, genericError);
            return View(form);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during login for {Email}", email);
            ModelState.AddModelError(string.Empty, "An unexpected error occurred. Please try again later.");
            return View(form);
        }
    }

    [Authorize, HttpPost, ValidateAntiForgeryToken]
    public async Task<IActionResult> Logout()
    {
        await _signIn.SignOutAsync();
        return RedirectToAction("Report", "Requests");
    }
}
