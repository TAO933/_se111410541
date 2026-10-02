母專案連結 [https://github.com/se-test-111310514/git-examples/commits/main/](https://github.com/TAO933/git-example/commits/main/)
* 分支 [https://github.com/se-test-111310514/git-examples/commits/developGitBranch](https://github.com/111410541se/git-example/commits/developGitBranch)

子專案連結 [https://github.com/Nickh2k6/git-examples-fork](https://github.com/TAO933/git-example/commits/main/)


## 母專案
1. git remote -v
2. git checkout -b developGitBranch
3. git branch
4. git add gitBranch.md
5. git commit -m "add gitBranch.md"
6. git push origin developGitBranch
7. git checkout main
8. git merge developGitBranch
9. git push origin main
## 子專案 fork
1. git clone git@github.com:TAO933/git-example.git
2. git add -A
3. cd git-example
4. git add .
5. git commit -m "add TAOFork.md"
6. git push
