SELECT (SELECT COUNT(*) FROM "User") AS users,
       (SELECT COUNT(*) FROM "Ticket") AS tickets,
       (SELECT COUNT(*) FROM "Employee") AS emps,
       (SELECT COUNT(*) FROM "KnowledgeArticle") AS kb;
SELECT "Id","Username","Role","MustChangePassword","FailedLoginAttempts","LockedUntil"
FROM "User" WHERE "Username" IN ('admin','tech.ivanov','client.ooo');
