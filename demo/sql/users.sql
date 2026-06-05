SELECT u."Id", u."Username", u."Role", u."MustChangePassword", u."LockedUntil",
       e."Name" AS emp_name, c."Name" AS client_name
FROM "User" u
LEFT JOIN "Employee" e ON e."UserId" = u."Id"
LEFT JOIN "Client" c ON c."UserId" = u."Id"
ORDER BY u."Id";
