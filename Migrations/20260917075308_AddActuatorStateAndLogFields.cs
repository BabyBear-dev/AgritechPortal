using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace AgritechPortal.Migrations
{
    /// <inheritdoc />
    public partial class AddActuatorStateAndLogFields : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<bool>(
                name: "IsOn",
                table: "Actuators",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<DateTime>(
                name: "LastChangedAt",
                table: "Actuators",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Type",
                table: "Actuators",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<int>(
                name: "ActuatorId",
                table: "ActionLogs",
                type: "integer",
                nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "Source",
                table: "ActionLogs",
                type: "character varying(50)",
                maxLength: 50,
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "IsOn",
                table: "Actuators");

            migrationBuilder.DropColumn(
                name: "LastChangedAt",
                table: "Actuators");

            migrationBuilder.DropColumn(
                name: "Type",
                table: "Actuators");

            migrationBuilder.DropColumn(
                name: "ActuatorId",
                table: "ActionLogs");

            migrationBuilder.DropColumn(
                name: "Source",
                table: "ActionLogs");
        }
    }
}
