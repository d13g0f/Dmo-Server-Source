using DigitalWorldOnline.Commons.DTOs.Chat;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace DigitalWorldOnline.Infrastructure.ContextConfiguration.Security
{
    public class CommandMessageConfiguration : IEntityTypeConfiguration<CommandMessageDTO>
    {
        public void Configure(EntityTypeBuilder<CommandMessageDTO> builder)
        {
            builder
                .ToTable("CommandMessage", "Security")
                .HasKey(x => x.Id);

            builder
                .Property(x => x.Time)
                .HasColumnType("datetime2")
                .HasDefaultValueSql("getdate()")
                .IsRequired();

            builder
                .Property(x => x.Message)
                .HasColumnType("varchar")
                .HasMaxLength(200)
                .IsRequired();

            builder
                .HasOne(x => x.Character);

            //builder
                //.HasOne(x => x.Name);
        }
    }
}