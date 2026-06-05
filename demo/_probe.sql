SELECT (SELECT count(*) FROM "User") AS users,
       (SELECT count(*) FROM "Ticket") AS tickets,
       (SELECT count(*) FROM "KnowledgeArticle") AS articles,
       (SELECT count(*) FROM "Employee") AS employees,
       (SELECT count(*) FROM "Client") AS clients;
SELECT "Id","Username","Role","MustChangePassword","IsEmailVerified" FROM "User" ORDER BY "Id";
