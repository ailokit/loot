globalThis.plugin = {
  onOpen() {
    loot.ui.setState({
      greeting: "填写个人资料，试试动态绑定、条件显示和 repeat 列表。",
      draftStatus: "演示已就绪",
      roleNotice: "当前角色：designer"
    });
  },

  onAction(action, payload) {
    const params = payload.params ?? {};

    switch (action) {
      case "createGreeting": {
        const name = String(params.name ?? payload.form?.name ?? "朋友").slice(0, 80);
        const nickname = String(params.nickname ?? payload.form?.nickname ?? "").slice(0, 80);
        const role = String(params.role ?? payload.form?.role ?? "成员");
        loot.ui.setState({
          greeting: `你好，${name}${nickname ? `（${nickname}）` : ""}！你的角色是 ${role}。`,
          draftStatus: `最近保存：${new Date().toLocaleTimeString()}`
        });
        loot.ui.notify("问候已生成");
        break;
      }

      case "roleChanged": {
        const role = String(params.role ?? payload.form?.role ?? "member");
        loot.ui.setState({ roleNotice: `watch 已检测到角色变化：${role}` });
        break;
      }

      case "saveDraft":
        loot.ui.setState({ draftStatus: `草稿已保存（触发来源：${String(params.source ?? "手动操作")}）` });
        break;

      case "resetDemo":
        loot.ui.setState({
          form: {
            name: "林默",
            nickname: "小默",
            account: "linmo@example.com",
            password: "loot-demo",
            age: 28,
            role: "designer",
            plan: "pro",
            contactMethod: "email",
            birthday: "1998-06-18",
            bio: "热爱整理资料，也喜欢把重复工作交给小工具。",
            score: 76
          },
          preferences: { notifications: true, compactMode: false, pinned: true, volume: 35 },
          greeting: "个人资料已恢复为示例值。",
          draftStatus: "尚未保存",
          roleNotice: "当前角色：designer"
        });
        loot.ui.notify("示例状态已重置");
        break;

      case "showNotice":
        loot.ui.setState({ roleNotice: String(params.message ?? "按钮 action 已触发") });
        loot.ui.notify(String(params.message ?? "按钮 action 已触发"));
        break;
    }
  }
};
