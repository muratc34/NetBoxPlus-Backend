﻿// <auto-generated />
using AuthAPI.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AuthAPI.Migrations
{
    [DbContext(typeof(AuthContext))]
    [Migration("20230529084224_init")]
    partial class init
    {
        /// <inheritdoc />
        protected override void BuildTargetModel(ModelBuilder modelBuilder)
        {
#pragma warning disable 612, 618
            modelBuilder
                .HasAnnotation("ProductVersion", "7.0.5")
                .HasAnnotation("Relational:MaxIdentifierLength", 63);

            NpgsqlModelBuilderExtensions.UseIdentityByDefaultColumns(modelBuilder);

            modelBuilder.Entity("AuthAPI.Model.Contracts.CreditCard", b =>
                {
                    b.Property<Guid>("CreditCardId")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<string>("CreditCardBrand")
                        .HasColumnType("text");

                    b.Property<string>("CreditCardName")
                        .HasColumnType("text");

                    b.Property<string>("CreditCardNumber")
                        .HasColumnType("text");

                    b.HasKey("CreditCardId");

                    b.ToTable("CreditCards");
                });

            modelBuilder.Entity("AuthAPI.Model.OperationClaim", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<string>("Name")
                        .HasColumnType("text");

                    b.HasKey("Id");

                    b.ToTable("OperationClaims", (string)null);

                    b.HasData(
                        new
                        {
                            Id = new Guid("0dcb249a-00dd-427b-a096-5df2b5742d91"),
                            Name = "Admin"
                        },
                        new
                        {
                            Id = new Guid("65e1c0c8-4d30-4edb-b428-92ea4a28d604"),
                            Name = "Kullanıcı"
                        });
                });

            modelBuilder.Entity("AuthAPI.Model.Profile", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<byte[]>("PinHash")
                        .IsRequired()
                        .HasColumnType("bytea");

                    b.Property<byte[]>("PinSalt")
                        .IsRequired()
                        .HasColumnType("bytea");

                    b.Property<string>("ProfileName")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<Guid>("UserId")
                        .HasColumnType("uuid");

                    b.HasKey("Id");

                    b.HasIndex("UserId");

                    b.ToTable("Profiles", (string)null);

                    b.HasData(
                        new
                        {
                            Id = new Guid("66bf0e71-b945-4acf-85ba-8e08c26fad18"),
                            PinHash = new byte[] { 218, 223, 11, 8, 202, 97, 134, 184, 190, 103, 215, 28, 154, 10, 223, 211, 218, 214, 164, 2, 168, 191, 18, 73, 233, 252, 5, 247, 186, 26, 189, 199, 176, 210, 179, 39, 182, 89, 91, 102, 15, 161, 246, 128, 14, 137, 117, 176, 62, 153, 170, 9, 217, 159, 25, 123, 110, 69, 112, 155, 71, 81, 183, 187 },
                            PinSalt = new byte[] { 131, 180, 247, 154, 23, 166, 209, 197, 209, 207, 16, 137, 66, 208, 103, 202, 183, 253, 8, 228, 165, 253, 92, 211, 131, 178, 147, 58, 215, 199, 55, 195, 116, 141, 113, 122, 235, 186, 189, 26, 249, 65, 108, 72, 220, 206, 144, 83, 60, 174, 170, 185, 182, 130, 245, 114, 230, 123, 80, 174, 69, 80, 169, 39, 73, 198, 37, 89, 160, 229, 76, 200, 146, 147, 203, 203, 74, 117, 162, 216, 126, 177, 36, 31, 16, 55, 69, 142, 221, 223, 200, 71, 122, 10, 63, 45, 50, 5, 134, 175, 29, 4, 213, 232, 10, 107, 145, 67, 248, 149, 179, 6, 99, 218, 238, 211, 230, 12, 109, 44, 160, 227, 26, 127, 19, 205, 238, 66 },
                            ProfileName = "Default",
                            UserId = new Guid("21da20b1-7503-4dd4-827e-1f0bcdddba8b")
                        },
                        new
                        {
                            Id = new Guid("a383c73c-2dc2-407c-a47b-103c201b93d6"),
                            PinHash = new byte[] { 22, 200, 83, 173, 54, 242, 33, 65, 206, 13, 201, 54, 40, 50, 89, 128, 7, 29, 80, 237, 203, 193, 6, 164, 221, 1, 193, 47, 136, 235, 2, 179, 171, 150, 77, 181, 177, 89, 220, 60, 4, 173, 127, 22, 40, 89, 7, 166, 136, 65, 242, 137, 182, 154, 12, 79, 210, 173, 205, 228, 92, 201, 204, 8 },
                            PinSalt = new byte[] { 131, 180, 247, 154, 23, 166, 209, 197, 209, 207, 16, 137, 66, 208, 103, 202, 183, 253, 8, 228, 165, 253, 92, 211, 131, 178, 147, 58, 215, 199, 55, 195, 116, 141, 113, 122, 235, 186, 189, 26, 249, 65, 108, 72, 220, 206, 144, 83, 60, 174, 170, 185, 182, 130, 245, 114, 230, 123, 80, 174, 69, 80, 169, 39, 73, 198, 37, 89, 160, 229, 76, 200, 146, 147, 203, 203, 74, 117, 162, 216, 126, 177, 36, 31, 16, 55, 69, 142, 221, 223, 200, 71, 122, 10, 63, 45, 50, 5, 134, 175, 29, 4, 213, 232, 10, 107, 145, 67, 248, 149, 179, 6, 99, 218, 238, 211, 230, 12, 109, 44, 160, 227, 26, 127, 19, 205, 238, 66 },
                            ProfileName = "Test",
                            UserId = new Guid("21da20b1-7503-4dd4-827e-1f0bcdddba8b")
                        });
                });

            modelBuilder.Entity("AuthAPI.Model.User", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<string>("Email")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("FirstName")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<string>("LastName")
                        .IsRequired()
                        .HasColumnType("text");

                    b.Property<byte[]>("PasswordHash")
                        .HasColumnType("bytea");

                    b.Property<byte[]>("PasswordSalt")
                        .HasColumnType("bytea");

                    b.Property<Guid>("PaymentId")
                        .HasColumnType("uuid");

                    b.Property<bool>("Status")
                        .HasColumnType("boolean");

                    b.Property<Guid?>("SubscriptionId")
                        .HasColumnType("uuid");

                    b.HasKey("Id");

                    b.ToTable("Users", (string)null);

                    b.HasData(
                        new
                        {
                            Id = new Guid("cae0ce15-1958-49dd-8abb-d82be6129764"),
                            Email = "admin@admin.com",
                            FirstName = "Admin",
                            LastName = "Admin",
                            PasswordHash = new byte[] { 179, 15, 57, 2, 244, 241, 230, 226, 58, 8, 5, 13, 232, 237, 114, 18, 173, 126, 77, 184, 143, 250, 180, 28, 214, 16, 102, 64, 245, 171, 155, 27, 77, 25, 159, 253, 120, 229, 229, 125, 36, 216, 162, 167, 182, 0, 244, 63, 109, 73, 20, 76, 158, 138, 80, 33, 225, 194, 124, 35, 150, 174, 233, 204 },
                            PasswordSalt = new byte[] { 131, 180, 247, 154, 23, 166, 209, 197, 209, 207, 16, 137, 66, 208, 103, 202, 183, 253, 8, 228, 165, 253, 92, 211, 131, 178, 147, 58, 215, 199, 55, 195, 116, 141, 113, 122, 235, 186, 189, 26, 249, 65, 108, 72, 220, 206, 144, 83, 60, 174, 170, 185, 182, 130, 245, 114, 230, 123, 80, 174, 69, 80, 169, 39, 73, 198, 37, 89, 160, 229, 76, 200, 146, 147, 203, 203, 74, 117, 162, 216, 126, 177, 36, 31, 16, 55, 69, 142, 221, 223, 200, 71, 122, 10, 63, 45, 50, 5, 134, 175, 29, 4, 213, 232, 10, 107, 145, 67, 248, 149, 179, 6, 99, 218, 238, 211, 230, 12, 109, 44, 160, 227, 26, 127, 19, 205, 238, 66 },
                            PaymentId = new Guid("597c84d5-e472-4fc3-9d6f-d72a8501eb91"),
                            Status = false
                        },
                        new
                        {
                            Id = new Guid("21da20b1-7503-4dd4-827e-1f0bcdddba8b"),
                            Email = "murat@murat.com",
                            FirstName = "Murat",
                            LastName = "Cinek",
                            PasswordHash = new byte[] { 159, 173, 87, 160, 192, 215, 69, 118, 59, 18, 169, 128, 135, 221, 92, 9, 107, 83, 143, 88, 96, 64, 231, 22, 150, 206, 46, 165, 10, 219, 23, 128, 251, 227, 226, 14, 37, 81, 92, 191, 76, 192, 113, 95, 251, 255, 168, 113, 35, 94, 117, 211, 86, 57, 148, 61, 126, 156, 80, 219, 77, 150, 90, 216 },
                            PasswordSalt = new byte[] { 131, 180, 247, 154, 23, 166, 209, 197, 209, 207, 16, 137, 66, 208, 103, 202, 183, 253, 8, 228, 165, 253, 92, 211, 131, 178, 147, 58, 215, 199, 55, 195, 116, 141, 113, 122, 235, 186, 189, 26, 249, 65, 108, 72, 220, 206, 144, 83, 60, 174, 170, 185, 182, 130, 245, 114, 230, 123, 80, 174, 69, 80, 169, 39, 73, 198, 37, 89, 160, 229, 76, 200, 146, 147, 203, 203, 74, 117, 162, 216, 126, 177, 36, 31, 16, 55, 69, 142, 221, 223, 200, 71, 122, 10, 63, 45, 50, 5, 134, 175, 29, 4, 213, 232, 10, 107, 145, 67, 248, 149, 179, 6, 99, 218, 238, 211, 230, 12, 109, 44, 160, 227, 26, 127, 19, 205, 238, 66 },
                            PaymentId = new Guid("2102fbea-73a7-468d-8e16-7e639e0b61cb"),
                            Status = false
                        });
                });

            modelBuilder.Entity("AuthAPI.Model.UserOperationClaim", b =>
                {
                    b.Property<Guid>("Id")
                        .ValueGeneratedOnAdd()
                        .HasColumnType("uuid");

                    b.Property<Guid>("OperationClaimId")
                        .HasColumnType("uuid");

                    b.Property<Guid>("UserId")
                        .HasColumnType("uuid");

                    b.HasKey("Id");

                    b.HasIndex("OperationClaimId");

                    b.HasIndex("UserId");

                    b.ToTable("OperationClaimsUser");

                    b.HasData(
                        new
                        {
                            Id = new Guid("f5c908ec-2841-4041-af77-b4696e0d52f3"),
                            OperationClaimId = new Guid("0dcb249a-00dd-427b-a096-5df2b5742d91"),
                            UserId = new Guid("cae0ce15-1958-49dd-8abb-d82be6129764")
                        },
                        new
                        {
                            Id = new Guid("157bd11f-7acf-4009-91be-90090aa8a787"),
                            OperationClaimId = new Guid("65e1c0c8-4d30-4edb-b428-92ea4a28d604"),
                            UserId = new Guid("21da20b1-7503-4dd4-827e-1f0bcdddba8b")
                        });
                });

            modelBuilder.Entity("OperationClaimUser", b =>
                {
                    b.Property<Guid>("OperationClaimsId")
                        .HasColumnType("uuid");

                    b.Property<Guid>("UsersId")
                        .HasColumnType("uuid");

                    b.HasKey("OperationClaimsId", "UsersId");

                    b.HasIndex("UsersId");

                    b.ToTable("OperationClaimUser");
                });

            modelBuilder.Entity("AuthAPI.Model.Profile", b =>
                {
                    b.HasOne("AuthAPI.Model.User", null)
                        .WithMany("Profiles")
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("AuthAPI.Model.UserOperationClaim", b =>
                {
                    b.HasOne("AuthAPI.Model.OperationClaim", "OperationClaim")
                        .WithMany()
                        .HasForeignKey("OperationClaimId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("AuthAPI.Model.User", "User")
                        .WithMany()
                        .HasForeignKey("UserId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.Navigation("OperationClaim");

                    b.Navigation("User");
                });

            modelBuilder.Entity("OperationClaimUser", b =>
                {
                    b.HasOne("AuthAPI.Model.OperationClaim", null)
                        .WithMany()
                        .HasForeignKey("OperationClaimsId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();

                    b.HasOne("AuthAPI.Model.User", null)
                        .WithMany()
                        .HasForeignKey("UsersId")
                        .OnDelete(DeleteBehavior.Cascade)
                        .IsRequired();
                });

            modelBuilder.Entity("AuthAPI.Model.User", b =>
                {
                    b.Navigation("Profiles");
                });
#pragma warning restore 612, 618
        }
    }
}
