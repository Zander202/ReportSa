using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Municipal.Web.Models;
namespace Municipal.Web.Controllers;
public class AccountController(SignInManager<IdentityUser> signIn, UserManager<IdentityUser> users) : Controller
{
    [HttpGet] public IActionResult Login() => View(new LoginForm());
    [HttpPost] public async Task<IActionResult> Login(LoginForm form)
    {
        if (!ModelState.IsValid) return View(form);
        var user = await users.FindByEmailAsync(form.Email.Trim());
        if (user is not null && await users.IsInRoleAsync(user, "Administrator")) {
            var result = await signIn.PasswordSignInAsync(user, form.Password, false, true);
            if (result.Succeeded) return RedirectToAction("Index", "Admin");
        }
        ModelState.AddModelError("", "Login unsuccessful. Check your credentials or try again later.");
        return View(form);
    }
    [Authorize, HttpPost] public async Task<IActionResult> Logout() { await signIn.SignOutAsync(); return RedirectToAction("Report", "Requests"); }
    [HttpGet] public IActionResult Denied() { Response.StatusCode = 403; return View(); }
}
