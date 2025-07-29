using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace NTG.Agent.Admin.Data
{
    public class ApplicationDbContext(DbContextOptions<ApplicationDbContext> options) : IdentityDbContext<ApplicationUser>(options)
    {
        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            modelBuilder.Entity<IdentityRole>().HasData(new IdentityRole
            {
                Id = new Guid("d5147680-87f5-41dc-aff2-e041959c2fa1").ToString(),
                Name = "Admin",
                NormalizedName = "ADMIN"
            });

            modelBuilder.Entity<ApplicationUser>().HasData(new ApplicationUser
            {
                Id = new Guid("e0afe23f-b53c-4ad8-b718-cb4ff5bb9f71").ToString(),
                UserName = "admin@ngtagent.com",
                Email = "admin@ngtagent.com",
                NormalizedEmail = "ADMIN@NTGAGENT.COM",
                NormalizedUserName = "ADMIN@NTGAGENT.COM",
                AccessFailedCount = 0, 
                ConcurrencyStamp = "101cd6ae-a8ef-4a37-97fd-04ac2dd630e4", 
                EmailConfirmed = true,
                LockoutEnabled = true, 
                PasswordHash = "AQAAAAIAAYagAAAAEFsVZF5T07uT8PrK0tMUqluwlOJO4XIk/fjLX7QuyYlbKtSI01akUmzGGhOrF5j6lg==", //Ntg@123
                PhoneNumberConfirmed = false, 
                SecurityStamp = "a9565acb-cee6-425f-9833-419a793f5fba", 
                TwoFactorEnabled = false,
            });

            modelBuilder.Entity<IdentityUserRole<string>>().HasData(new IdentityUserRole<string>
            {
                UserId = "e0afe23f-b53c-4ad8-b718-cb4ff5bb9f71",
                RoleId = "d5147680-87f5-41dc-aff2-e041959c2fa1"
            });
        }
    }
}
