# Validação da implementação

Eduardo Pires Escudero — RM 556527. Desenvolvimento assistido por Codex.

- Build: sem erros ou warnings após correções.
- Testes unitários: 41 casos, sem banco/rede/serviços externos.
- Exercício HTTP local: 73 verificações positivas e negativas.
- Autoria assistida declarada no README; configuração oficial de qualidade preservada.

## Problemas encontrados e corrigidos

1. O Range decimal dependia da cultura pt-BR no Windows. Os limites e valores agora usam cultura invariável;
   testes unitários verificam pt-BR e en-US.
2. IDs de histórico gerados pela aplicação precisavam de ValueGeneratedNever no mapeamento EF.
   Sem isso, a edição tratava novas entradas como updates. A validação HTTP confirmou criação, edição,
   envio, aprovação e pagamento com cinco registros de histórico.
3. Alterações de roles agora usam transação para tornar remoção e adição uma operação atômica.
4. Concorrência otimista usa Revision: transições repetidas falham sem histórico duplicado.

## Casos HTTP verificados

Cadastro sem promoção por propriedade extra; login; usuário sem role; rotas anônimas;
Admin sem acesso funcional; administração restrita; roles desconhecidas; remoção da própria role Admin;
proprietário/estado não controlados pelo cliente; edição e envio próprios; leitura fora do escopo 404;
repetição 409; decisões sem perfil 403; pagamento repetido; histórico autorizado; autoaprovação,
autorreprovação e autopagamento bloqueados; justificativa inválida; reprovação válida;
valor zero/acima do limite; descrição curta; data futura; categoria inexistente;
listagem por Employee, Approver, Finance e Auditor.

Os testes HTTP são revisão adicional e não substituem os unitários pontuados.
O resultado definitivo de qualidade deve ser lido no artefato do workflow do SHA final da main.
