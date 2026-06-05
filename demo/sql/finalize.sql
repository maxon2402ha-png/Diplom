UPDATE "User" SET "MustChangePassword" = false, "FailedLoginAttempts" = 0, "LockedUntil" = NULL;
SELECT u."Id", u."Username", u."Role", u."MustChangePassword",
       (SELECT COUNT(*) FROM "Ticket" t WHERE t."AssigneeEmployeeId" = e."Id") AS assigned
FROM "User" u
LEFT JOIN "Employee" e ON e."UserId" = u."Id"
ORDER BY u."Id";
