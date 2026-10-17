using Microsoft.EntityFrameworkCore;
using AgritechPortal.Models;

namespace AgritechPortal.Data
{
    public class AppDbContext : DbContext
    {
        public AppDbContext(DbContextOptions<AppDbContext> options) : base(options) { }

        public DbSet<Operator> Operators { get; set; }
        public DbSet<Farm> Farms { get; set; }
        public DbSet<Greenhouse> Greenhouses { get; set; }
        public DbSet<Zone> Zones { get; set; }
        public DbSet<SafetyLimit> SafetyLimits { get; set; }
        public DbSet<Actuator> Actuators { get; set; }
        public DbSet<ActuatorRelay> ActuatorRelays { get; set; }
        public DbSet<SensorReading> SensorReadings { get; set; }
        public DbSet<SensorTelemetry> SensorTelemetries { get; set; }
        public DbSet<Recommendation> Recommendations { get; set; }
        public DbSet<ActionLog> ActionLogs { get; set; }
        public DbSet<FarmMemory> FarmMemories { get; set; }
        public DbSet<SensorDevice> SensorDevices { get; set; }
        public DbSet<WeatherData> WeatherData { get; set; }

        // ✅ Per-user chat history
        public DbSet<ChatMessage> ChatMessages { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            base.OnModelCreating(modelBuilder);

            // ✅ Index for fast per-user history lookups
            modelBuilder.Entity<ChatMessage>()
                .HasIndex(m => new { m.OperatorEmail, m.CreatedAt });

            // ✅ Index for fast conversation lookup
            modelBuilder.Entity<ChatMessage>()
                .HasIndex(m => new { m.OperatorEmail, m.ConversationId, m.CreatedAt });

            // ✅ Cap content length
            modelBuilder.Entity<ChatMessage>()
                .Property(m => m.Content)
                .HasMaxLength(8000);
        }
    }
}