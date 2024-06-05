using System.Data.SqlTypes;
using GLib;
using MultiRoom2.Entities;
using MultistreamConferenceTestService.Util;
using WebApplication1;
using DateTime = System.DateTime;

namespace MultiRoom2.Services;

public class ProfileService
{
    private DbContext db;
    private MultistreamConferenceConfiguration config;
    private MailService mailService;

    public ProfileService(DbContext db, MultistreamConferenceConfiguration config, MailService mailService)
    {
        this.db = db;
        this.config = config;
        this.mailService = mailService;
    }

    public void Register(RegisterSpecType regData)
    {
        var loginKey = regData.Login.ToLower();
        if (db.UserInfos.FirstOrDefault(u => u.LoginKey == loginKey) != null)
        {
            throw new ApplicationException("User " + regData.Login + " already exists");
        }
        else
        {
            // var salt = Convert.ToBase64String(_ctx.SecureRandom.GenerateRandomBytes(64));

            var user = new UserInfo()
            {
                Activated = false,
                HashSalt = "",
                Email = regData.Email,
                IsDeleted = false,
                RegistrationStamp = DateTime.UtcNow,
                Login = regData.Login,
                LoginKey = loginKey,
                PasswordHash = regData.Password,
                Password = regData.Password,
                LastLoginStamp = SqlDateTime.MinValue.Value,
                LastTokenStamp = SqlDateTime.MinValue.Value
            };
            db.UserInfos.Add(user);
            db.SaveChanges();

            RequestActivationImpl(user, regData.Email);
            db.SaveChanges();
        }
    }

    public void RequestAccess(ResetPasswordSpecType spec)
    {
        var loginKey = spec.Login.ToLower();
        
        var user = db.UserInfos.FirstOrDefault(u => u.LoginKey == loginKey);
        if (user != null && user.Email == spec.Email && !user.IsDeleted)
        {
            var accessRestoreToken = MakeToken();

            user.LastToken = accessRestoreToken;
            user.LastTokenStamp = DateTime.UtcNow;
            user.LastTokenKind = DbUserTokenKind.AccessRestore;
            db.SaveChanges();
            

            mailService.SendMail(
                spec.Email, "Access restore",
                "To regain access to your profile follow this link: " + string.Format(config.LinkTemplates.PasswordRestoreLink, accessRestoreToken)
            );
        }
        else
        {
            throw new ApplicationException("User not found or incorrect email");
        }
    }

    public OkType Activate(string? key)
    {
        var user = db.UserInfos.FirstOrDefault(u => u.LastToken == key);

        if (user is { LastTokenKind: DbUserTokenKind.Activation })
        {
            if (user.Activated)
                throw new ApplicationException("Already activated");

            if (user.LastTokenStamp + config.TokenTimeout >= DateTime.UtcNow)
            {
                user.LastLoginStamp = DateTime.UtcNow;
                user.LastToken = null;
                user.Activated = true;
                db.SaveChanges();
            }
            else
            {
                throw new ApplicationException("Acivation token expired");
            }
        }
        else
        {
            throw new ApplicationException("Invalid activation token");
        }

        return new OkType();
    }

    public OkType RestoreAccess(string? key)
    {
        var user = db.UserInfos.FirstOrDefault(u => u.LastToken == key);

        if (user is { LastTokenKind: DbUserTokenKind.AccessRestore })
        {
            if (user.LastTokenStamp + config.TokenTimeout >= DateTime.UtcNow)
            {
                user.LastLoginStamp = DateTime.UtcNow;
                user.LastToken = null;
                db.SaveChanges();
            }
            else
            {
                throw new ApplicationException("Acivation token expired");
            }
        }
        else
        {
            throw new ApplicationException("Invalid activation token");
        }

        return new OkType();
    }

    public void DeleteProfile(long userId)
    {
        var user = db.UserInfos.First(u => u.Id == userId);

        user.IsDeleted = true;
        user.LastToken = null;
        
        db.SaveChanges();
    }
    
    public long Login(LoginSpecType loginSpec)
    {
        var loginKey = loginSpec.Login;
        var user = db.UserInfos.FirstOrDefault(u => u.LoginKey == loginKey);

        if (user != null && user.PasswordHash == loginSpec.Password && !user.IsDeleted)
        {
            user.LastLoginStamp = DateTime.UtcNow;
            db.SaveChanges();
            
            // todo: set cookie

            return user.Id;
        }
        else
        {
            throw new ApplicationException("Invalid credentials");
        }
    }
    
    public void RequestActivation(RequestActivationSpecType spec, long userId)
    {
        RequestActivationImpl(db.UserInfos.First(u => u.Id == userId), spec.Email);
        db.SaveChanges();
    }
    
    public void SetEmail(long userId, ChangeEmailSpecType spec)
    {
        var user = db.UserInfos.First(u => u.Id == userId);

        if (user.Email == spec.OldEmail &&
            user.PasswordHash == spec.Password)
        {
            user.Email = spec.NewEmail;
            db.SaveChanges();
        }
        else
        {
            throw new ApplicationException("Invalid old email or password");
        }
    }
    
    public void SetPassword(long userId, ChangePasswordSpecType spec)
    {
        var user = db.UserInfos.First(u => u.Id == userId);

        if (user.Email == spec.Email)
            // user.PasswordHash == spec.OldPassword.ComputeSha256Hash(user.HashSalt))
        {
            user.PasswordHash = spec.NewPassword;
            db.SaveChanges();

            mailService.SendMail(spec.Email, "Password was changed", "Dear " + user.Login + ", your password was changed!");
        }
        else
        {
            throw new ApplicationException("Invalid old email");
        }
    }
    
    public ProfileFootprintInfoType GetProfileFootprint(long userId)
    {
        var user = db.UserInfos.First(u => u.Id == userId);

        var parts = user.Email.Split('@');
        var leading = parts[0].Substring(0, Math.Min(2, parts[0].Length));
        var suffixDotPos = parts[1].LastIndexOf('.');
        var ending = suffixDotPos > 0 ? parts[1].Substring(suffixDotPos) : parts[1].Substring(parts[1].Length - Math.Min(2, parts[1].Length));
        var emailFootprint = leading + "***@***" + ending;

        return new ProfileFootprintInfoType() {
            Login = user.Login,
            EmailFootprint = emailFootprint,
            IsActivated = user.Activated
        };
    }
    
    private void RequestActivationImpl(UserInfo user, string email)
    {
        if (user.Activated)
            throw new ApplicationException("Already activated");

        var activationToken = this.MakeToken();

        user.LastToken = activationToken;
        user.LastTokenStamp = DateTime.UtcNow;
        user.LastTokenKind = DbUserTokenKind.Activation;

        mailService.SendMail(
            email, "Registration activation",
            "To confirm your registration follow this link: " + string.Format(config.LinkTemplates.ActivationLink, activationToken)
        );
    }
    
    private static Random random = new Random();
    
    private string MakeToken()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        return new string(Enumerable.Repeat(chars, 64)
            .Select(s => s[random.Next(s.Length)]).ToArray());
        // return new[] { "=", "/", "+" }.Aggregate(Convert.ToBase64String(_ctx.SecureRandom.GenerateRandomBytes(64)), (s, c) => s.Replace(c, string.Empty));
    }

    public ListType GetAllUsers()
    {
        var users = db.UserInfos.Select(it => it.TranslateUser()).ToArray();
        return new ListType()
        {
            Count = users.Length,
            From = 0,
            Items = users,
            TotalCount = users.Length
        };
    }
}